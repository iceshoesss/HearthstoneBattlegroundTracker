using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HBT
{


/// <summary>
/// 对局记录持久化（games.json）
/// </summary>
public static class GameStore
{
    private static readonly object _lock = new object();
    private static string _path = "";
    private static List<GameRecord> _cache;
    private static bool _cacheDirty = true;

    /// <summary>
    /// 初始化（指定文件路径，通常在 exe 同目录）
    /// </summary>
    public static void Init(string? dir = null)
    {
        var baseDir = dir ?? AppDomain.CurrentDomain.BaseDirectory;
        _path = Path.Combine(baseDir, "games.json");
        _cacheDirty = true;
    }

    /// <summary>
    /// 保存今日起始 MMR（写入 games.json 作为特殊记录，同一天同账号不重复写入）
    /// </summary>
    public static void SaveTodayStartMmr(int mmr, string battleTag = "")
    {
        try
        {
            if (string.IsNullOrEmpty(_path)) Init();
            var today = DateTime.Now.ToString("yyyy-MM-dd");

            // 检查今天是否已保存（匹配 battleTag）
            lock (_lock)
            {
                if (File.Exists(_path))
                {
                    foreach (var line in File.ReadLines(_path, Encoding.UTF8))
                    {
                        if (line.Contains("\"type\":\"daily_mmr\"")
                            && line.Contains($"\"date\":\"{today}\"")
                            && (string.IsNullOrEmpty(battleTag) || line.Contains($"\"battleTag\":\"{Esc(battleTag)}\"")))
                            return; // 今天已保存
                    }
                }
                var entry = $"{{\"type\":\"daily_mmr\",\"date\":\"{today}\",\"battleTag\":\"{Esc(battleTag)}\",\"mmr\":{mmr}}}";
                File.AppendAllText(_path, entry + "\n", Encoding.UTF8);
            }
        }
        catch { }
    }

    /// <summary>
    /// 获取今日起始 MMR（从 games.json 中读取，按账号过滤）
    /// </summary>
    public static int GetTodayStartMmr(string battleTag = "")
    {
        try
        {
            if (string.IsNullOrEmpty(_path)) Init();
            if (!File.Exists(_path)) return 0;
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            lock (_lock)
            {
                foreach (var line in File.ReadLines(_path, Encoding.UTF8))
                {
                    if (line.Contains("\"type\":\"daily_mmr\"")
                        && line.Contains($"\"date\":\"{today}\"")
                        && (string.IsNullOrEmpty(battleTag) || line.Contains($"\"battleTag\":\"{Esc(battleTag)}\"")))
                    {
                        return ExtractInt(line, "mmr");
                    }
                }
            }
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 加载所有记录（带缓存，兼容旧 JSON 数组格式 + 新 JSONL 格式）
    /// </summary>
    public static List<GameRecord> Load()
    {
        lock (_lock)
        {
            if (!_cacheDirty && _cache != null)
                return new List<GameRecord>(_cache);

            if (string.IsNullOrEmpty(_path)) Init();
            if (!File.Exists(_path)) { _cache = new List<GameRecord>(); _cacheDirty = false; return new List<GameRecord>(_cache); }

            try
            {
                var text = File.ReadAllText(_path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) { _cache = new List<GameRecord>(); _cacheDirty = false; return new List<GameRecord>(_cache); }

                var trimmed = text.TrimStart();
                _cache = trimmed.StartsWith("[")
                    ? ParseJsonArray(text)   // 旧格式兼容
                    : ParseJsonLines(text);  // 新 JSONL 格式
                _cacheDirty = false;
                return new List<GameRecord>(_cache);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[GameStore] 加载失败: {e.Message}");
                return new List<GameRecord>();
            }
        }
    }

    /// <summary>
    /// 追加一条记录（JSONL 格式，每行一条，避免全量重写）
    /// </summary>
    public static void Save(GameRecord record)
    {
        if (string.IsNullOrEmpty(_path)) Init();

        var line = RecordToJson(record);
        lock (_lock)
        {
            File.AppendAllText(_path, line + "\n", Encoding.UTF8);
            _cacheDirty = true; // 标记缓存失效
        }

        Console.WriteLine($"[GameStore] 已保存: 第{record.Placement}名 {record.HeroName} MMR:{record.RatingChange:+#;-#;0}");
    }

    /// <summary>
    /// 获取今天的记录（本地时间，按账号过滤）
    /// </summary>
    public static List<GameRecord> GetToday(string battleTag = "")
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var all = Load();
        var result = new List<GameRecord>();
        foreach (var r in all)
        {
            if (!string.IsNullOrEmpty(battleTag) && r.BattleTag != battleTag) continue;
            // 时间戳是 UTC ISO 格式，转为本地时间再比较
            if (DateTime.TryParse(r.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                && dt.ToLocalTime().ToString("yyyy-MM-dd") == today)
            {
                result.Add(r);
            }
        }
        return result;
    }

    /// <summary>
    /// 获取最近 N 条记录（倒序，按账号过滤）
    /// 如果最近一条记录超过 4 小时，返回空列表
    /// </summary>
    public static List<GameRecord> GetRecent(int count = 5, string battleTag = "")
    {
        var all = Load();
        var result = new List<GameRecord>();

        // 从后往前找最近的记录
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(battleTag) && all[i].BattleTag != battleTag) continue;

            // 找到第一条有效记录，检查时间
            if (DateTime.TryParse(all[i].Timestamp, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                var localTime = dt.ToLocalTime();
                var hoursSinceLastGame = (DateTime.Now - localTime).TotalHours;

                // 如果最近一条记录超过 4 小时，返回空列表
                if (hoursSinceLastGame > 4)
                    return new List<GameRecord>();
            }

            // 时间检查通过，开始收集记录
            result.Add(all[i]);

            // 继续收集剩余记录
            for (int j = i - 1; j >= 0 && result.Count < count; j--)
            {
                if (!string.IsNullOrEmpty(battleTag) && all[j].BattleTag != battleTag) continue;
                result.Add(all[j]);
            }

            break;
        }

        return result;
    }

    /// <summary>
    /// 单条记录序列化为 JSON 字符串
    /// </summary>
    private static string RecordToJson(GameRecord r)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"battleTag\":\"{Esc(r.BattleTag)}\",");
        sb.Append($"\"heroName\":\"{Esc(r.HeroName)}\",");
        sb.Append($"\"heroCardId\":\"{Esc(r.HeroCardId)}\",");
        sb.Append($"\"placement\":{r.Placement},");
        sb.Append($"\"points\":{r.Points},");
        sb.Append($"\"rating\":{r.Rating},");
        sb.Append($"\"ratingAfter\":{r.RatingAfter},");
        sb.Append($"\"ratingChange\":{r.RatingChange},");
        sb.Append($"\"gameUuid\":\"{Esc(r.GameUuid)}\",");
        sb.Append($"\"mode\":\"{Esc(r.Mode)}\",");
        sb.Append($"\"timestamp\":\"{Esc(r.Timestamp)}\"");
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// 解析 JSONL 格式（每行一条 JSON）
    /// </summary>
    private static List<GameRecord> ParseJsonLines(string text)
    {
        var result = new List<GameRecord>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("{")) continue;
            if (trimmed.Contains("\"type\":\"daily_mmr\"")) continue; // 跳过每日MMR记录
            var rec = ParseRecord(trimmed);
            if (rec != null) result.Add(rec);
        }
        return result;
    }

    // ── 极简 JSON 序列化（无第三方依赖） ──

    private static List<GameRecord> ParseJsonArray(string text)
    {
        var result = new List<GameRecord>();
        // 极简解析：逐个 { ... } 块
        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf('{', i);
            if (start < 0) break;
            int end = text.IndexOf('}', start);
            if (end < 0) break;

            var block = text.Substring(start, end - start + 1);
            var rec = ParseRecord(block);
            if (rec != null) result.Add(rec);

            i = end + 1;
        }
        return result;
    }

    private static GameRecord ParseRecord(string block)
    {
        try
        {
            var r = new GameRecord();
            r.BattleTag = Extract(block, "battleTag");
            r.HeroName = Extract(block, "heroName");
            r.HeroCardId = Extract(block, "heroCardId");
            r.Placement = ExtractInt(block, "placement");
            r.Points = ExtractInt(block, "points");
            r.Rating = ExtractInt(block, "rating");
            r.RatingAfter = ExtractInt(block, "ratingAfter");
            r.RatingChange = ExtractInt(block, "ratingChange");
            r.GameUuid = Extract(block, "gameUuid");
            r.Mode = Extract(block, "mode");
            r.Timestamp = Extract(block, "timestamp");
            return r;
        }
        catch { return null; }
    }

    private static string Extract(string json, string key)
    {
        var search = $"\"{key}\":\"";
        var idx = json.IndexOf(search);
        if (idx < 0) return "";
        idx += search.Length;
        var end = json.IndexOf('"', idx);
        if (end < 0) return "";
        return json.Substring(idx, end - idx);
    }

    private static int ExtractInt(string json, string key)
    {
        var search = $"\"{key}\":";
        var idx = json.IndexOf(search);
        if (idx < 0) return 0;
        idx += search.Length;
        var end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
            end++;
        if (end == idx) return 0;
        int.TryParse(json.Substring(idx, end - idx), out var val);
        return val;
    }

    private static string Esc(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

}
