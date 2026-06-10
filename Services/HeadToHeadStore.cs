using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HBT.Models;

namespace HBT.Services
{

/// <summary>
/// 对战胜负记录持久化（{localPlayerLo}.json）
/// 格式: { "opponentLo": { "Wins": N, "Losses": N } }
/// </summary>
public static class HeadToHeadStore
{
    private static readonly object _lock = new object();
    private static string _path = "";

    /// <summary>初始化（指定本地玩家 Lo，文件路径 = exeDir/{Lo}.json）</summary>
    public static void Init(ulong localPlayerLo)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _path = Path.Combine(baseDir, $"{localPlayerLo}.json");
    }

    /// <summary>记录一局胜负</summary>
    public static void RecordGame(ulong opponentLo, bool won)
    {
        if (opponentLo == 0 || string.IsNullOrEmpty(_path)) return;

        lock (_lock)
        {
            var dict = LoadInternal();
            if (!dict.TryGetValue(opponentLo, out var rec))
            {
                rec = new HeadToHeadRecord();
                dict[opponentLo] = rec;
            }

            if (won) rec.Wins++;
            else rec.Losses++;

            SaveInternal(dict);
        }
    }

    /// <summary>查询某对手的胜负记录（无记录返回 0-0）</summary>
    public static HeadToHeadRecord GetRecord(ulong opponentLo)
    {
        if (opponentLo == 0 || string.IsNullOrEmpty(_path))
            return new HeadToHeadRecord();

        lock (_lock)
        {
            var dict = LoadInternal();
            if (dict.TryGetValue(opponentLo, out var rec))
                return rec;
            return new HeadToHeadRecord();
        }
    }

    // ── 内部读写 ──

    private static Dictionary<ulong, HeadToHeadRecord> LoadInternal()
    {
        var result = new Dictionary<ulong, HeadToHeadRecord>();
        if (!File.Exists(_path)) return result;

        try
        {
            var text = File.ReadAllText(_path, Encoding.UTF8).Trim();
            if (string.IsNullOrEmpty(text) || !text.StartsWith("{"))
                return result;

            // 极简解析: { "key": {"Wins":1,"Losses":0}, ... }
            int i = 1; // skip opening {
            while (i < text.Length)
            {
                // 找 key（对手 Lo）
                var keyStart = text.IndexOf('"', i);
                if (keyStart < 0) break;
                keyStart++;
                var keyEnd = text.IndexOf('"', keyStart);
                if (keyEnd < 0) break;
                var keyStr = text.Substring(keyStart, keyEnd - keyStart);
                if (!ulong.TryParse(keyStr, out var lo)) { i = keyEnd + 1; continue; }

                // 找内部 { ... }
                var objStart = text.IndexOf('{', keyEnd);
                if (objStart < 0) break;
                var objEnd = FindMatchingBrace(text, objStart);
                if (objEnd < 0) break;

                var objBlock = text.Substring(objStart, objEnd - objStart + 1);
                var wins = ExtractInt(objBlock, "Wins");
                var losses = ExtractInt(objBlock, "Losses");
                result[lo] = new HeadToHeadRecord { Wins = wins, Losses = losses };

                i = objEnd + 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HeadToHeadStore] 加载失败: {ex.Message}");
        }
        return result;
    }

    private static void SaveInternal(Dictionary<ulong, HeadToHeadRecord> dict)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"{kvp.Key}\":{{\"Wins\":{kvp.Value.Wins},\"Losses\":{kvp.Value.Losses}}}");
            }
            sb.Append('}');
            File.WriteAllText(_path, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HeadToHeadStore] 保存失败: {ex.Message}");
        }
    }

    private static int FindMatchingBrace(string text, int openPos)
    {
        int depth = 1;
        for (int i = openPos + 1; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int ExtractInt(string json, string key)
    {
        var search = $"\"{key}\":";
        var idx = json.IndexOf(search);
        if (idx < 0) return 0;
        idx += search.Length;
        var end = idx;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        if (end == idx) return 0;
        int.TryParse(json.Substring(idx, end - idx), out var val);
        return val;
    }
}

}
