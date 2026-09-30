using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace HBT.Services
{

/// <summary>
/// BattlegroundDB（卡牌库）自动更新
/// 下载新 DLL → 退出本程序 → 批处理替换 → 再启动
/// </summary>
public class BgdbUpdateService
{
    private const string ApiBaseUrl = "https://api.iceshoes.dpdns.org";
    private static readonly HttpClient _http;

    static BgdbUpdateService()
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        try { _http.DefaultRequestHeaders.UserAgent.ParseAdd("HBT-BgdbUpdate/1.0"); } catch { }
    }

    private static string ExeDir =>
        Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetExecutingAssembly().Location);

    private static string ExePath =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(ExeDir ?? ".", "HearthstoneBattlegroundTracker.exe");

    private static string DllPath => Path.Combine(ExeDir, "BattlegroundDB.dll");
    private static string NewDllPath => DllPath + ".new";
    private static string OldDllPath => DllPath + ".old";
    private static string UpdaterBat => Path.Combine(ExeDir, "_update_bgdb.bat");

    public static bool HasPendingUpdate => File.Exists(NewDllPath);

    /// <summary>启动后异步检查并提示更新</summary>
    public static async Task CheckAndPromptAsync()
    {
        try
        {
            if (HasPendingUpdate)
            {
                Console.WriteLine("[BgdbUpdate] 已有 .new 待应用，跳过检查");
                return;
            }

            var currentVersion = GetCurrentVersion();
            var latestVersion = await GetLatestVersionFromHsbgAsync();

            Console.WriteLine($"[BgdbUpdate] 版本比较: 本地='{currentVersion}' vs 最新='{latestVersion}'");

            if (string.IsNullOrEmpty(latestVersion))
            {
                Console.WriteLine("[BgdbUpdate] 无法获取最新版本，跳过");
                return;
            }
            // 本地版本读不到时仍允许更新；相同则跳过
            if (!string.IsNullOrEmpty(currentVersion) && currentVersion == latestVersion)
            {
                Console.WriteLine("[BgdbUpdate] 已是最新版本");
                return;
            }

            Console.WriteLine($"[BgdbUpdate] 发现新版本: {currentVersion ?? "(未知)"} → {latestVersion}");

            var result = MessageBox.Show(
                $"BattlegroundDB 卡牌库有新版本！\n\n当前: {currentVersion ?? "(未知)"}\n最新: {latestVersion}\n\n「是」立即更新并重启工具，「否」稍后再说。",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes) return;

            Console.WriteLine("[BgdbUpdate] 开始下载…");
            var bytes = await _http.GetByteArrayAsync($"{ApiBaseUrl}/download");
            if (bytes == null || bytes.Length < 1000)
                throw new Exception("下载内容过小，可能失败");
            File.WriteAllBytes(NewDllPath, bytes);
            Console.WriteLine($"[BgdbUpdate] 已下载 {bytes.Length} 字节 → {NewDllPath}");

            // 批处理：等进程真正退出 → 替换 DLL → 再启动（HBT_UPDATING=1 避免「已在运行」）
            var pid = Process.GetCurrentProcess().Id;
            var bat = $@"@echo off
setlocal
set HBT_UPDATING=1
set EXE=""{ExePath}""
set DLL=""{DllPath}""
set NEW=""{NewDllPath}""
set OLD=""{OldDllPath}""

rem 等待本进程退出（最多 40 秒）
set /a _w=0
:waitloop
tasklist /fi ""PID eq {pid}"" 2>nul | find ""{pid}"" >nul
if errorlevel 1 goto swap
timeout /t 1 /nobreak >nul
set /a _w+=1
if %_w% lss 40 goto waitloop

:swap
rem 再等一小段，确保句柄/互斥体释放
timeout /t 2 /nobreak >nul

if exist %OLD% del /f /q %OLD%
if exist %DLL% ren %DLL% BattlegroundDB.dll.old
if not exist %DLL% if exist %NEW% ren %NEW% BattlegroundDB.dll
if exist %OLD% del /f /q %OLD%

start """" %EXE%

timeout /t 3 /nobreak >nul
del /f /q ""%~f0""
";
            File.WriteAllText(UpdaterBat, bat, System.Text.Encoding.Default);

            Process.Start(new ProcessStartInfo
            {
                FileName = UpdaterBat,
                WorkingDirectory = ExeDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = true,
            });

            Console.WriteLine("[BgdbUpdate] 更新脚本已启动，正在退出以应用更新…");
            try
            {
                MessageBox.Show(
                    "正在应用卡牌库更新…\n\n程序将自动关闭并重新打开，请稍候。",
                    "更新中",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch { }
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BgdbUpdate] 检查/更新失败: {ex.Message}");
            try { MessageBox.Show("卡牌库更新失败：\n" + ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
        }
    }

    private static string GetCurrentVersion()
    {
        try
        {
            var asm = typeof(BattlegroundDB.Cards).Assembly;
            using var stream = asm.GetManifestResourceStream("BattlegroundDB.Data.bg_cards.json");
            if (stream == null)
            {
                Console.WriteLine("[BgdbUpdate] 未找到嵌入资源 bg_cards.json");
                return null;
            }
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var match = System.Text.RegularExpressions.Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BgdbUpdate] 读取本地版本失败: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> GetLatestVersionFromHsbgAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://hsbg.cards/api/v1/patches");
            Console.WriteLine($"[BgdbUpdate] hsbg.cards 响应长度: {json?.Length ?? 0}");
            var patches = JsonConvert.DeserializeObject<PatchesResponse>(json);
            var version = patches?.Data?.FirstOrDefault()?.CurrentPatch;
            Console.WriteLine($"[BgdbUpdate] 解析版本: '{version}'");
            return version;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BgdbUpdate] 获取 hsbg.cards 版本失败: {ex.Message}");
            return null;
        }
    }

    private class PatchesResponse
    {
        [JsonProperty("data")] public List<PatchInfo> Data { get; set; }
    }

    private class PatchInfo
    {
        [JsonProperty("currentPatch")] public string CurrentPatch { get; set; }
    }
}

}
