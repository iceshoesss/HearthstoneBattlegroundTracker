using System;
using System.IO;

namespace HBT
{

/// <summary>
/// 运行时配置（当前仅 API 地址）
///
/// 查找顺序：
/// 1. 环境变量 BGTRACKER_CONFIG 指定的路径
/// 2. 从 exe 目录向上逐级查找 shared_config.json（最多 5 级）
/// 3. exe 同目录的 config.json
/// 4. exe 同目录的 config.json.example
/// </summary>
public class Config
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";
    /// <summary>测试：check-league 前 remap 等待组为真实路人（全流程仿真）</summary>
    public bool TestRemap { get; set; }

    /// <summary>最近一次 Load 结果（供 ApiClient 读开关）</summary>
    public static Config Current { get; private set; }

    public static Config Load()
    {
        // 1. 环境变量指定
        var envPath = Environment.GetEnvironmentVariable("BGTRACKER_CONFIG");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            Console.WriteLine($"[Config] 使用环境变量指定: {envPath}");
            Current = Parse(envPath);
            return Current;
        }

        // 2. 从 exe 目录向上查找 shared_config.json
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var shared = Path.Combine(dir, "shared_config.json");
            if (File.Exists(shared))
            {
                Console.WriteLine($"[Config] 找到共享配置: {shared}");
                Current = Parse(shared);
                return Current;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        // 3. exe 同目录 config.json
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var local = Path.Combine(exeDir, "config.json");
        if (File.Exists(local))
        {
            Console.WriteLine($"[Config] 使用本地配置: {local}");
            Current = Parse(local);
            return Current;
        }

        // 4. config.json.example
        var example = Path.Combine(exeDir, "config.json.example");
        if (File.Exists(example))
        {
            Console.WriteLine($"[Config] config.json 不存在，使用 example");
            Current = Parse(example);
            return Current;
        }

        Console.WriteLine("[Config] 未找到任何配置文件，使用默认值");
        Current = new Config();
        return Current;
    }

    private static Config Parse(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var cfg = new Config();

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"")) continue;

                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;

                var key = trimmed.Substring(1, trimmed.IndexOf('"', 1) - 1).Trim();
                var valPart = trimmed.Substring(colonIdx + 1).Trim().TrimEnd(',');
                var val = valPart.Trim('"');

                if (key == "apiBaseUrl")
                    cfg.ApiBaseUrl = val;
                else if (key == "testRemap")
                    cfg.TestRemap = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            Console.WriteLine($"[Config] apiBaseUrl={cfg.ApiBaseUrl} testRemap={cfg.TestRemap}");
            return cfg;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Config] 解析失败: {e.Message}，使用默认值");
            return new Config();
        }
    }
}

}
