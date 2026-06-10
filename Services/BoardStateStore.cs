using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HBT
{

/// <summary>
/// 最终阵容持久化（boardstate.json，JSONL 格式）
/// 最多保留 20 条，超出删除最早的
/// </summary>
public static class BoardStateStore
{
    private static readonly object _lock = new object();
    private static string _path = "";
    private const int MaxRecords = 20;

    public static void Init(string dir = null)
    {
        var baseDir = dir ?? AppDomain.CurrentDomain.BaseDirectory;
        _path = Path.Combine(baseDir, "boardstate.json");
    }

    /// <summary>
    /// 保存一局的最终阵容
    /// </summary>
    public static void Save(string gameUuid, string battleTag, List<Dictionary<string, object>> boardState)
    {
        if (string.IsNullOrEmpty(_path)) Init();
        if (boardState == null || boardState.Count == 0) return;

        try
        {
            var sb = new StringBuilder();
            sb.Append("{\"gameUuid\":\"").Append(Esc(gameUuid)).Append('"');
            sb.Append(",\"battleTag\":\"").Append(Esc(battleTag)).Append('"');
            sb.Append(",\"timestamp\":\"").Append(DateTime.UtcNow.ToString("o")).Append('"');
            sb.Append(",\"boardState\":");
            sb.Append(SerializeBoardState(boardState));
            sb.Append('}');

            lock (_lock)
            {
                File.AppendAllText(_path, sb.ToString() + "\n", Encoding.UTF8);
                TrimToMax();
            }
        }
        catch { }
    }

    /// <summary>
    /// 获取最近 N 条记录（倒序）
    /// </summary>
    public static List<BoardStateRecord> GetRecent(int count = 9, string battleTag = "")
    {
        if (string.IsNullOrEmpty(_path)) Init();
        var result = new List<BoardStateRecord>();

        try
        {
            if (!File.Exists(_path)) return result;

            List<string> lines;
            lock (_lock)
            {
                lines = new List<string>(File.ReadAllLines(_path, Encoding.UTF8));
            }

            for (int i = lines.Count - 1; i >= 0 && result.Count < count; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || !line.StartsWith("{")) continue;

                var record = ParseRecord(line);
                if (record == null) continue;
                if (!string.IsNullOrEmpty(battleTag) && record.BattleTag != battleTag) continue;
                result.Add(record);
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// 根据 gameUuid 获取阵容
    /// </summary>
    public static BoardStateRecord GetByGameUuid(string gameUuid)
    {
        if (string.IsNullOrEmpty(_path)) Init();

        try
        {
            if (!File.Exists(_path)) return null;

            string[] lines;
            lock (_lock)
            {
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Contains($"\"gameUuid\":\"{Esc(gameUuid)}\""))
                    return ParseRecord(line);
            }
        }
        catch { }

        return null;
    }

    // ── 内部方法 ──

    private static void TrimToMax()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var lines = new List<string>(File.ReadAllLines(_path, Encoding.UTF8));
            if (lines.Count <= MaxRecords) return;

            var trimmed = lines.GetRange(lines.Count - MaxRecords, MaxRecords);
            File.WriteAllLines(_path, trimmed.ToArray(), Encoding.UTF8);
        }
        catch { }
    }

    private static string SerializeBoardState(List<Dictionary<string, object>> boardState)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < boardState.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var m = boardState[i];
            sb.Append('{');
            bool first = true;
            foreach (var kv in m)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append('"').Append(':');
                if (kv.Value is bool b) sb.Append(b ? "true" : "false");
                else if (kv.Value is int n) sb.Append(n);
                else sb.Append('"').Append(Esc(kv.Value?.ToString() ?? "")).Append('"');
            }
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static BoardStateRecord ParseRecord(string line)
    {
        try
        {
            var record = new BoardStateRecord();
            record.GameUuid = Extract(line, "gameUuid");
            record.BattleTag = Extract(line, "battleTag");
            record.Timestamp = Extract(line, "timestamp");

            // 提取 boardState 数组
            var bsStart = line.IndexOf("\"boardState\":");
            if (bsStart < 0) return record;
            bsStart = line.IndexOf('[', bsStart);
            if (bsStart < 0) return record;

            var depth = 0;
            var bsEnd = bsStart;
            for (int i = bsStart; i < line.Length; i++)
            {
                if (line[i] == '[') depth++;
                else if (line[i] == ']') depth--;
                if (depth == 0) { bsEnd = i; break; }
            }

            var bsJson = line.Substring(bsStart, bsEnd - bsStart + 1);
            record.BoardState = ParseBoardState(bsJson);
            return record;
        }
        catch { return null; }
    }

    private static List<Dictionary<string, object>> ParseBoardState(string json)
    {
        var result = new List<Dictionary<string, object>>();
        int i = 0;
        while (i < json.Length)
        {
            int objStart = json.IndexOf('{', i);
            if (objStart < 0) break;
            int depth = 0;
            int objEnd = objStart;
            for (int j = objStart; j < json.Length; j++)
            {
                if (json[j] == '{') depth++;
                else if (json[j] == '}') depth--;
                if (depth == 0) { objEnd = j; break; }
            }

            var objStr = json.Substring(objStart, objEnd - objStart + 1);
            var minion = new Dictionary<string, object>();
            minion["cardId"] = Extract(objStr, "cardId");
            minion["attack"] = ExtractInt(objStr, "attack");
            minion["health"] = ExtractInt(objStr, "health");
            minion["techLevel"] = ExtractInt(objStr, "techLevel");
            minion["golden"] = objStr.Contains("\"golden\":true");
            minion["taunt"] = objStr.Contains("\"taunt\":true");
            minion["divineShield"] = objStr.Contains("\"divineShield\":true");
            minion["poisonous"] = objStr.Contains("\"poisonous\":true");
            minion["venomous"] = objStr.Contains("\"venomous\":true");
            minion["windfury"] = objStr.Contains("\"windfury\":true");
            minion["reborn"] = objStr.Contains("\"reborn\":true");
            minion["stealth"] = objStr.Contains("\"stealth\":true");
            minion["deathrattle"] = objStr.Contains("\"deathrattle\":true");
            result.Add(minion);

            i = objEnd + 1;
        }
        return result;
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

public class BoardStateRecord
{
    public string GameUuid { get; set; } = "";
    public string BattleTag { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public List<Dictionary<string, object>> BoardState { get; set; } = new List<Dictionary<string, object>>();
}

}
