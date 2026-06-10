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
/// BattlegroundDB 自动更新服务
/// </summary>
public class BgdbUpdateService
{
    private const string ApiBaseUrl = "https://api.iceshoes.dpdns.org";
    private static readonly HttpClient _http;

    static BgdbUpdateService()
    {
        // 启用 TLS 1.2（hsbg.cards 需要）
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        _http = new HttpClient();
    }

    private static string DllDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static string DllPath => Path.Combine(DllDir, "BattlegroundDB.dll");
    private static string NewDllPath => DllPath + ".new";
    private static string UpdaterBat => Path.Combine(DllDir, "_update_bgdb.bat");

    /// <summary>检查是否有待应用的更新（启动时调用）</summary>
    public static bool HasPendingUpdate => File.Exists(NewDllPath);

    /// <summary>检查并提示更新（启动后异步调用）</summary>
    public static async Task CheckAndPromptAsync()
    {
        try
        {
            // 有待应用的更新，不重复检查
            if (HasPendingUpdate)
            {
                Console.WriteLine("[BgdbUpdate] 检测到 .new 文件，跳过检查");
                return;
            }

            var currentVersion = GetCurrentVersion();
            var latestVersion = await GetLatestVersionFromHsbgAsync();

            Console.WriteLine($"[BgdbUpdate] 版本比较: 本地='{currentVersion}' vs 最新='{latestVersion}'");

            if (string.IsNullOrEmpty(latestVersion) || currentVersion == latestVersion)
            {
                Console.WriteLine("[BgdbUpdate] 版本相同或无法获取最新版本，跳过");
                return;
            }

            Console.WriteLine($"[BgdbUpdate] 发现新版本: {currentVersion} → {latestVersion}");

            // 弹窗提示
            var result = MessageBox.Show(
                $"BattlegroundDB 有新版本可用！\n\n当前版本: {currentVersion}\n最新版本: {latestVersion}\n\n点击「是」立即更新，点击「否」跳过。",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                // 下载新版本
                var bytes = await _http.GetByteArrayAsync($"{ApiBaseUrl}/download");
                File.WriteAllBytes(NewDllPath, bytes);

                // 创建批处理：等 HBT 退出后替换 DLL、重启 HBT
                var exePath = Assembly.GetExecutingAssembly().Location;
                var bat = $@"
@echo off
timeout /t 2 /nobreak > nul
ren ""{DllPath}"" BattlegroundDB.dll.old
ren ""{NewDllPath}"" BattlegroundDB.dll
start """" ""{exePath}""
timeout /t 3 /nobreak > nul
del ""{DllPath}.old""
del ""%~f0""
";
                File.WriteAllText(UpdaterBat, bat);

                // 启动批处理
                Process.Start(new ProcessStartInfo
                {
                    FileName = UpdaterBat,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                // 退出 HBT
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BgdbUpdate] 检查失败: {ex.Message}");
        }
    }

    private static string GetCurrentVersion()
    {
        try
        {
            // 直接从嵌入资源读取版本号，不依赖 Cards.Load()（避免 System.Text.Json 缺失问题）
            var asm = typeof(BattlegroundDB.Cards).Assembly;
            using var stream = asm.GetManifestResourceStream("BattlegroundDB.Data.bg_cards.json");
            if (stream == null) return null;
            using var reader = new System.IO.StreamReader(stream);
            var json = reader.ReadToEnd();
            // 简单提取 "version": "xxx"
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
