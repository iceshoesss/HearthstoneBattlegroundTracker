using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HBT
{


/// <summary>
/// Flask API 客户端（check-league + update-placement）
/// </summary>
public static class ApiClient
{
    /// <summary>界面日志（MainWindow 挂上 AppendLog）</summary>
    public static Action<string> OnLog;

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        try { OnLog?.Invoke(msg); } catch { }
    }
    // 配置
    private static string _baseUrl = "";
    public static string BaseUrl => _baseUrl;

    // API Key 编译时写入，不暴露给用户配置
    // 发布前需替换为实际 Key
    private const string ApiKey = "";

    private static string _pluginVersion = "0.7.0"; // 服务端兼容版本，LeagueTool 实际版本另算

    static ApiClient()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    }

    private static readonly HttpClient _http = new HttpClient(
        new HttpClientHandler { UseProxy = false }
    ) { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// 用 Lo 集合生成确定性 UUID（同一局游戏的 8 个 Lo → 同一个 UUID）
    /// </summary>
    public static string GenerateDeterministicUuid(List<LobbyPlayer> lobbyPlayers)
    {
        var los = lobbyPlayers
            .Where(p => p.Lo != 0)
            .Select(p => p.Lo.ToString())
            .OrderBy(s => s)
            .ToList();
        var input = string.Join(",", los);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            // 取前 16 字节，按 UUID 格式输出
            return string.Format("{0:x2}{1:x2}{2:x2}{3:x2}-{4:x2}{5:x2}-{6:x2}{7:x2}-{8:x2}{9:x2}-{10:x2}{11:x2}{12:x2}{13:x2}{14:x2}{15:x2}",
                hash[0], hash[1], hash[2], hash[3],
                hash[4], hash[5],
                hash[6], hash[7],
                hash[8], hash[9],
                hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]);
        }
    }

    /// <summary>
    /// 根据 accountIdHi 计算 region 代码
    /// </summary>
    public static int GetRegionFromAccountIdHi(ulong accountIdHi) => (int)((accountIdHi >> 32) & 0xFF);

    /// <summary>
    /// 将 region 代码转换为区域字符串
    /// </summary>
    public static string GetRegionString(int region) => region switch
    {
        1 => "US",
        2 => "EU",
        3 => "ASIA",
        5 => "CN",
        _ => "UNKNOWN"
    };

    // 状态
    public static string VerificationCode { get; private set; } = "";
    public static bool LastLeagueResult { get; private set; }
    public static string LastError { get; private set; } = "";
    public static string ServerGameUuid { get; private set; } = "";  // 服务端返回的 gameUuid

    /// <summary>
    /// 初始化配置（从配置文件或默认值）
    /// </summary>
    public static void Init(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// 测试与服务器的连通性
    /// </summary>
    public static async Task<bool> PingAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/");
            request.Headers.Add("X-HDT-Plugin", _pluginVersion);
            if (!string.IsNullOrEmpty(ApiKey))
                request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>测试：check-league 前把等待组脚本位 remap 成真实路人</summary>
    public static bool TestRemap
    {
        get
        {
            // 1) shared_config.json / config.json 里的 "testRemap": true
            if (Config.Current != null && Config.Current.TestRemap) return true;
            // 2) exe 目录 test_remap.on
            try
            {
                var flag = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_remap.on");
                if (File.Exists(flag)) return true;
            }
            catch { }
            // 3) 环境变量
            var v = Environment.GetEnvironmentVariable("TEST_REMAP");
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>启动时打一次，方便排查开关是否生效</summary>
    public static void LogTestRemapEnv()
    {
        var v = Environment.GetEnvironmentVariable("TEST_REMAP") ?? "(null)";
        var file = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_remap.on"));
        Log($"[API] TestRemap={TestRemap} (cfg={Config.Current?.TestRemap} test_remap.on={file} env='{v}') pid={System.Diagnostics.Process.GetCurrentProcess().Id}");
    }

    /// <summary>把非自己的大厅路人写入等待组（测试用）</summary>
    public static async Task<bool> RemapLosAsync(string selfTag, List<Dictionary<string, object>> strangers)
    {
        if (!TestRemap)
        {
            Log("[API] TEST_REMAP 未启用，跳过 remap-los");
            return true;
        }
        if (strangers == null || strangers.Count == 0) return true;
        try
        {
            var body = new Dictionary<string, object>
            {
                ["battleTag"] = selfTag ?? "",
                ["players"] = strangers,
            };
            Log($"[API] test remap-los：{strangers.Count} 个路人 → 等待组脚本位");
            string json = null;
            var ws = MatchSessionClient.Active;
            if (ws != null && ws.IsConnected)
            {
                json = await ws.RequestAsync("remap-los", body, 8000);
                if (json != null) Log("[API] remap-los 走 WS 成功");
            }
            if (json == null)
            {
                var (ok, hjson) = await PostAsync("/api/plugin/test/remap-los", body);
                if (!ok) return false;
                json = hjson;
                Log("[API] remap-los 走 HTTP 成功");
            }
            Log("[API] test remap-los 完成: " + json);
            return json.Contains("\"ok\"") && json.Contains("true");
        }
        catch (Exception e)
        {
            LastError = "remap-los 异常: " + e.Message;
            Log("[API] ⚠️ " + LastError);
            return false;
        }
    }

    /// <summary>
    /// 英雄选定后调用，检查是否为联赛对局
    /// </summary>
    public static async Task<bool?> CheckLeagueAsync(
        string playerId,
        ulong accountIdLo,
        List<LobbyPlayer> lobbyPlayers,
        string region = "CN",
        string mode = "solo",
        string startedAt = "")
    {
        LastError = "";
        ServerGameUuid = "";

        // Lo 全为 0 时跳过（HearthMirror 未就绪）
        var validPlayers = lobbyPlayers.Where(p => p.Lo != 0).ToList();
        if (validPlayers.Count == 0)
        {
            Console.WriteLine("[API] 所有 accountIdLo 为 0（LobbyInfo 未就绪），跳过 check-league");
            return false;
        }

        // 构建 accountIdLoList
        var loList = new List<string>();
        var playersDict = new Dictionary<string, object>();

        foreach (var p in lobbyPlayers)
        {
            var loStr = p.Lo.ToString();
            loList.Add(loStr);
            var playerInfo = new Dictionary<string, object>
            {
                ["heroCardId"] = p.HeroCardId ?? ""
            };
            if (!string.IsNullOrEmpty(p.HeroName))
                playerInfo["heroName"] = p.HeroName;
            // 所有玩家都传 displayName（HearthMirror 可读取对手名字）
            if (!string.IsNullOrEmpty(p.DisplayName))
                playerInfo["displayName"] = p.DisplayName;
            // 本地玩家额外带 battleTag
            if (p.Lo == accountIdLo)
            {
                if (!string.IsNullOrEmpty(playerId))
                    playerInfo["battleTag"] = playerId;
            }
            playersDict[loStr] = playerInfo;
        }

        var body = new Dictionary<string, object>
        {
            ["accountIdLoList"] = loList,
            ["playerId"] = playerId,
            ["accountIdLo"] = accountIdLo.ToString(),
            ["players"] = playersDict,
            ["mode"] = mode,
            ["startedAt"] = startedAt ?? ""
        };

        // 测试：先 remap 等待组脚本位 → 真实路人 Lo，再 check-league
        Log(TestRemap
            ? "[API] TEST_REMAP=1，check-league 前将 remap 等待组"
            : "[API] TEST_REMAP 未开，check-league 使用原始等待组 Lo");
        if (TestRemap)
        {
            var strangers = new List<Dictionary<string, object>>();
            foreach (var p in validPlayers)
            {
                if (p.Lo == accountIdLo) continue;
                strangers.Add(new Dictionary<string, object>
                {
                    ["accountIdLo"] = p.Lo.ToString(),
                    ["battleTag"] = "",
                    ["displayName"] = p.DisplayName ?? "",
                });
            }
            var remapOk = await RemapLosAsync(playerId, strangers);
            Log(remapOk
                ? $"[API] test remap 完成，{strangers.Count} 个路人入桌"
                : "[API] ⚠️ test remap 失败，check-league 可能不匹配");
        }

        try
        {
            // 主通道：match WS（一场一连）；失败退回 HTTP
            string json = null;
            var ws = MatchSessionClient.Active;
            if (ws != null && ws.IsConnected)
            {
                json = await ws.RequestAsync("check-league", body, 10000);
                if (json != null) Console.WriteLine("[API] check-league 走 WS 成功");
            }
            if (json == null)
            {
                var (ok, hjson) = await PostAsync("/api/plugin/check-league", body);
                if (!ok) return null; // HTTP 错误，调用方可重试
                json = hjson;
            }

            // 解析响应
            // 无论 isLeague 结果如何，都提取 verificationCode
            // 服务端 check-league 总是返回 verificationCode（确保玩家在 player_records 中有记录）
            var vc = ExtractJsonString(json, "verificationCode");
            if (!string.IsNullOrEmpty(vc))
            {
                VerificationCode = vc;
                Console.WriteLine($"[API] ✅ 验证码: {VerificationCode}");
            }

            // 提取服务端返回的 gameUuid（淘汰赛由服务端生成）
            var serverUuid = ExtractJsonString(json, "gameUuid");
            if (!string.IsNullOrEmpty(serverUuid) && serverUuid != "null")
            {
                ServerGameUuid = serverUuid;
                Console.WriteLine($"[API] ✅ 服务端 gameUuid: {ServerGameUuid}");
            }

            // 必须精确解析 isLeague，不能用 Contains("true")（会误伤 ok:true）
            var isLeague = ExtractJsonBool(json, "isLeague");
            if (isLeague)
            {
                Console.WriteLine("[API] 联赛对局已匹配");
            }
            else
            {
                Console.WriteLine("[API] 非联赛对局，但验证码已获取");
            }

            LastLeagueResult = isLeague;
            return isLeague;
        }
        catch (Exception e)
        {
            LastError = $"check-league 异常: {e.Message}";
            Console.WriteLine($"[API] ⚠️ {LastError}");
            return null; // 网络异常，调用方可重试
        }
    }

    /// <summary>
    /// 启动时调用，初始化玩家信息并获取验证码
    /// </summary>
    public static async Task<bool> InitializePlayerAsync(
        string playerId,
        ulong accountIdHi,
        ulong accountIdLo,
        int rating = 0)
    {
        LastError = "";

        // 根据 accountIdHi 计算 region 字符串
        var regionCode = GetRegionFromAccountIdHi(accountIdHi);
        var region = GetRegionString(regionCode);

        var body = new Dictionary<string, object>
        {
            ["playerId"] = playerId,
            ["accountIdHi"] = accountIdHi.ToString(),
            ["accountIdLo"] = accountIdLo.ToString(),
            ["region"] = region,
            ["rating"] = rating
        };

        try
        {
            var (ok, json) = await PostAsync("/api/plugin/initialize-player", body);
            if (!ok) return false;

            var vc = ExtractJsonString(json, "verificationCode");
            if (!string.IsNullOrEmpty(vc))
            {
                VerificationCode = vc;
                Console.WriteLine("[API] ✅ 验证码: " + VerificationCode);
                return true;
            }
            else
            {
                Console.WriteLine("[API] ⚠️ initialize-player 响应中无 verificationCode，json=" + json);
            }
        }
        catch (Exception e)
        {
            LastError = "initialize-player 异常: " + e.Message;
            Console.WriteLine("[API] ⚠️ " + LastError);
        }
        return false;
    }

    /// <summary>
    /// 游戏结束时调用，上报排名
    /// </summary>
    public static async Task<bool> UpdatePlacementAsync(
        string gameUuid,
        string playerId,
        ulong accountIdLo,
        int placement,
        List<string> reconnectTimes = null,
        List<(ulong lo, int placement)> otherPlacements = null,
        bool timeout = false)
    {
        LastError = "";

        var body = new Dictionary<string, object>
        {
            ["gameUuid"] = gameUuid,
            ["accountIdLo"] = accountIdLo.ToString(),
            ["placement"] = placement,
            ["playerId"] = playerId
        };
        if (timeout)
            body["timeout"] = true;
        if (reconnectTimes != null && reconnectTimes.Count > 0)
            body["reconnectTimes"] = reconnectTimes;
        if (otherPlacements != null && otherPlacements.Count > 0)
        {
            var opList = new List<object>();
            foreach (var (lo, p) in otherPlacements)
                opList.Add(new Dictionary<string, object> { ["accountIdLo"] = lo.ToString(), ["placement"] = p });
            body["otherPlacements"] = opList;
        }

        // 重试 3 次
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                // 主通道 WS，失败退回 HTTP
                string json = null;
                var ws = MatchSessionClient.Active;
                if (ws != null && ws.IsConnected)
                {
                    json = await ws.RequestAsync("update-placement", body, 12000);
                    if (json != null) Console.WriteLine("[API] update-placement 走 WS 成功");
                }
                if (json == null)
                {
                    var (ok, hjson) = await PostAsync("/api/plugin/update-placement", body);
                    if (!ok)
                    {
                        if (attempt < 3)
                        {
                            Console.WriteLine($"[API] update-placement 失败，第 {attempt} 次重试...");
                            await Task.Delay(2000);
                            continue;
                        }
                        return false;
                    }
                    json = hjson;
                }

                var finalized = ExtractJsonBool(json, "finalized");
                Console.WriteLine($"[API] ✅ 排名已上传: 第 {placement} 名 | finalized={finalized}");
                return true;
            }
            catch (Exception e)
            {
                if (attempt < 3)
                {
                    Console.WriteLine($"[API] update-placement 异常，第 {attempt} 次重试: {e.Message}");
                    await Task.Delay(2000);
                    continue;
                }
                LastError = $"update-placement 异常: {e.Message}";
                Console.WriteLine($"[API] ⚠️ {LastError}");
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 构筑模式：检查是否为联赛对局（不需要 LobbyPlayers）
    /// </summary>
    public static async Task<bool?> CheckLeagueAsync(
        string playerId,
        ulong accountIdLo,
        ulong opponentAccountIdLo,
        string localDisplayName,
        string opponentDisplayName,
        string localBattleTag,
        string opponentBattleTag,
        string localHeroCardId,
        string opponentHeroCardId,
        string region = "CN",
        string mode = "constructed_standard",
        string startedAt = "")
    {
        LastError = "";
        ServerGameUuid = "";

        var playersDict = new Dictionary<string, object>();

        var localInfo = new Dictionary<string, object>
        {
            ["battleTag"] = localBattleTag,
            ["displayName"] = localDisplayName,
            ["heroCardId"] = localHeroCardId ?? ""
        };
        playersDict[accountIdLo.ToString()] = localInfo;

        var oppInfo = new Dictionary<string, object>
        {
            ["battleTag"] = opponentBattleTag,
            ["displayName"] = opponentDisplayName,
            ["heroCardId"] = opponentHeroCardId ?? ""
        };
        playersDict[opponentAccountIdLo.ToString()] = oppInfo;

        var body = new Dictionary<string, object>
        {
            ["accountIdLoList"] = new List<string> { accountIdLo.ToString(), opponentAccountIdLo.ToString() },
            ["playerId"] = playerId,
            ["accountIdLo"] = accountIdLo.ToString(),
            ["players"] = playersDict,
            ["mode"] = mode,
            ["startedAt"] = startedAt ?? ""
        };

        try
        {
            string json = null;
            var ws = MatchSessionClient.Active;
            if (ws != null && ws.IsConnected)
            {
                json = await ws.RequestAsync("check-league", body, 10000);
                if (json != null) Console.WriteLine("[API] check-league(constructed) 走 WS 成功");
            }
            if (json == null)
            {
                var (ok, hjson) = await PostAsync("/api/plugin/check-league", body);
                if (!ok) return null;
                json = hjson;
            }

            var vc = ExtractJsonString(json, "verificationCode");
            if (!string.IsNullOrEmpty(vc))
            {
                VerificationCode = vc;
                Console.WriteLine($"[API] ✅ 验证码: {VerificationCode}");
            }

            var serverUuid = ExtractJsonString(json, "gameUuid");
            if (!string.IsNullOrEmpty(serverUuid) && serverUuid != "null")
            {
                ServerGameUuid = serverUuid;
                Console.WriteLine($"[API] ✅ 服务端 gameUuid: {ServerGameUuid}");
            }

            var isLeague = ExtractJsonBool(json, "isLeague");
            if (isLeague)
                Console.WriteLine("[API] 联赛对局已匹配");
            else
                Console.WriteLine("[API] 非联赛对局，但验证码已获取");

            LastLeagueResult = isLeague;
            return isLeague;
        }
        catch (Exception e)
        {
            LastError = $"check-league 异常: {e.Message}";
            Console.WriteLine($"[API] ⚠️ {LastError}");
            return null;
        }
    }

    /// <summary>
    /// 构筑模式：上传双方排名
    /// </summary>
    public static async Task<bool> UpdatePlacementAsync(
        string gameUuid,
        string localTag, ulong localLo, int localPlacement,
        string oppTag, ulong oppLo, int oppPlacement,
        string mode = "constructed_standard")
    {
        LastError = "";

        var placements = new List<object>
        {
            new Dictionary<string, object>
            {
                ["playerId"] = localTag,
                ["accountIdLo"] = localLo.ToString(),
                ["placement"] = localPlacement,
                ["displayName"] = localTag.Contains("#") ? localTag.Substring(0, localTag.IndexOf("#")) : localTag,
            },
            new Dictionary<string, object>
            {
                ["playerId"] = oppTag,
                ["accountIdLo"] = oppLo.ToString(),
                ["placement"] = oppPlacement,
                ["displayName"] = oppTag.Contains("#") ? oppTag.Substring(0, oppTag.IndexOf("#")) : oppTag,
            },
        };

        var body = new Dictionary<string, object>
        {
            ["gameUuid"] = gameUuid,
            ["mode"] = mode,
            ["placements"] = placements,
        };

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                string json = null;
                var ws = MatchSessionClient.Active;
                if (ws != null && ws.IsConnected)
                {
                    json = await ws.RequestAsync("update-placement", body, 12000);
                    if (json != null) Console.WriteLine("[API] update-placement(constructed) 走 WS 成功");
                }
                if (json == null)
                {
                    var (ok, hjson) = await PostAsync("/api/plugin/update-placement", body);
                    if (ok) json = hjson;
                }
                if (json != null)
                {
                    Console.WriteLine($"[API] ✅ 双方结果已上传: 本机={localPlacement} 对手={oppPlacement}");
                    return true;
                }
                if (attempt < 3)
                {
                    Console.WriteLine($"[API] update-placement 失败，第 {attempt} 次重试...");
                    await Task.Delay(2000);
                }
            }
            catch (Exception e)
            {
                if (attempt < 3)
                {
                    Console.WriteLine($"[API] update-placement 重试异常: {e.Message}");
                    await Task.Delay(2000);
                    continue;
                }
                LastError = $"update-placement 异常: {e.Message}";
                Console.WriteLine($"[API] ⚠️ {LastError}");
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 上报游戏统计数据（rating、阵容、ratingChange）
    /// </summary>
    public static async Task<bool> ReportGameStatsAsync(
        string gameUuid,
        string playerId,
        ulong accountIdLo,
        int rating,
        List<object> boardState = null,
        Dictionary<string, object> ratingChange = null,
        int placement = 0,
        List<string> trinkets = null,
        int anomalyDbfId = 0,
        string heroCardId = "",
        string heroName = "")
    {
        LastError = "";

        var body = new Dictionary<string, object>
        {
            ["gameUuid"] = gameUuid,
            ["playerId"] = playerId,
            ["accountIdLo"] = accountIdLo.ToString(),
            ["rating"] = rating,
            ["heroCardId"] = heroCardId,
            ["heroName"] = heroName,
        };
        if (placement > 0)
            body["placement"] = placement;
        if (trinkets != null && trinkets.Count > 0)
            body["trinkets"] = trinkets;
        if (anomalyDbfId > 0)
            body["anomalyDbfId"] = anomalyDbfId;
        if (boardState != null && boardState.Count > 0)
            body["boardState"] = boardState;
        if (ratingChange != null && ratingChange.Count > 0)
            body["ratingChange"] = ratingChange;

        try
        {
            var (ok, json) = await PostAsync("/api/plugin/report-game-stats", body);
            if (!ok) return false;
            Console.WriteLine($"[API] ✅ 游戏统计已上报: rating={rating}, board={boardState?.Count ?? 0} cards");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[API] report-game-stats 异常: {e.Message}");
            return false;
        }
    }


    // ── Phase2：一场一连 — 报名 / 取消 / 名次确认 ──

    public class QueueJoinResult
    {
        public string Ticket = "";
        public string State = "";
        public string TableNamesJson = "";
    }

    public static async Task<QueueJoinResult> QueueJoinAsync(string battleTag)
    {
        var body = new Dictionary<string, object> { { "battleTag", battleTag } };
        var (ok, json) = await PostAsync("/api/plugin/queue/join", body);
        if (!ok) { LastError = json; return new QueueJoinResult(); }
        return new QueueJoinResult
        {
            Ticket = ExtractJsonString(json, "ticket"),
            State = ExtractJsonString(json, "state"),
            TableNamesJson = ExtractJsonArray(json, "tableNames"),
        };
    }

    public static async Task<bool> QueueLeaveAsync(string ticket)
    {
        var body = new Dictionary<string, object> { { "ticket", ticket } };
        var (ok, json) = await PostAsync("/api/plugin/queue/leave", body);
        return ok;
    }

    public static async Task<bool> ConfirmPlacementAsync(string ticket, int placement, bool okConfirm)
    {
        var body = new Dictionary<string, object>
        {
            { "ticket", ticket },
            { "placement", placement },
            { "ok", okConfirm },
        };
        var (ok, json) = await PostAsync("/api/plugin/confirm-placement", body);
        return ok;
    }

    // ── HTTP 工具方法 ──

    private static async Task<(bool ok, string json)> PostAsync(string path, Dictionary<string, object> body)
    {
        var url = _baseUrl + path;
        var jsonBody = SimpleJsonSerialize(body);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        Console.WriteLine($"[API] → {path} body={jsonBody}");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("X-HDT-Plugin", _pluginVersion);
        if (!string.IsNullOrEmpty(ApiKey))
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

        var response = await _http.SendAsync(request);
        var respBody = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"[API] ← {path} {(int)response.StatusCode} {respBody}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[API] ❌ {path} → {(int)response.StatusCode} {respBody}");
            return (false, respBody);
        }

        return (true, respBody);
    }

    // ── 极简 JSON 序列化（避免依赖第三方库） ──

    private static string SimpleJsonSerialize(Dictionary<string, object> dict)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var kv in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append('"').Append(':');
            AppendJsonValue(sb, kv.Value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendJsonValue(StringBuilder sb, object? val)
    {
        switch (val)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int i:
                sb.Append(i);
                break;
            case long l:
                sb.Append(l);
                break;
            case double d:
                sb.Append(d);
                break;
            case string s:
                sb.Append('"').Append(EscapeJsonString(s)).Append('"');
                break;
            case List<string> list:
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(EscapeJsonString(list[i])).Append('"');
                }
                sb.Append(']');
                break;
            case List<object> objList:
                sb.Append('[');
                for (int i = 0; i < objList.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendJsonValue(sb, objList[i]);
                }
                sb.Append(']');
                break;
            case Dictionary<string, object> sub:
                sb.Append(SimpleJsonSerialize(sub));
                break;
            default:
                sb.Append('"').Append(EscapeJsonString(val.ToString() ?? "")).Append('"');
                break;
        }
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string ExtractJsonArray(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var needle = "\"" + key + "\":";
        var idx = json.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0) return "";
        var bracket = json.IndexOf('[', idx);
        if (bracket < 0) return "";
        var end = json.IndexOf(']', bracket);
        if (end < 0) return "";
        return json.Substring(bracket, end - bracket + 1);
    }

    private static string ExtractJsonString(string json, string key)
    {
        // 搜索 "key" 并确保前面是 { 或 , 防止子串匹配
        var search = $"\"{key}\"";
        var idx = -1;
        var startIdx = 0;
        while (true)
        {
            idx = json.IndexOf(search, startIdx);
            if (idx < 0) return "";
            // 检查前面的字符：应该是 { , 或空白
            if (idx == 0) break;
            var prev = json[idx - 1];
            if (prev == '{' || prev == ',' || prev == ' ' || prev == '\n' || prev == '\r' || prev == '\t')
                break;
            startIdx = idx + 1;
        }
        idx += search.Length;
        // 跳过冒号与空白
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == ':' || json[idx] == '\t'))
            idx++;
        if (idx >= json.Length) return "";
        // null / true / false / 数字：不是字符串，不当成值
        if (json[idx] != '"')
        {
            if (json.Substring(idx).StartsWith("null")) return "";
            // 数字或字面量：直接返回 token（避免吞掉后面的 "season"）
            int j = idx;
            while (j < json.Length && json[j] != ',' && json[j] != '}' && json[j] != ' ' && json[j] != '\n')
                j++;
            return json.Substring(idx, j - idx).Trim();
        }
        idx++; // 跳过开始引号
        // 逐字符扫描，处理转义引号
        var sb = new StringBuilder();
        while (idx < json.Length)
        {
            var c = json[idx];
            if (c == '\\' && idx + 1 < json.Length)
            {
                sb.Append(json[idx + 1]); // 取转义后的字符
                idx += 2;
                continue;
            }
            if (c == '"') break; // 真正的结束引号
            sb.Append(c);
            idx++;
        }
        return sb.ToString();
    }

    private static bool ExtractJsonBool(string json, string key)
    {
        var search = $"\"{key}\"";
        var idx = json.IndexOf(search);
        if (idx < 0) return false;
        idx += search.Length;
        // 跳过 ": "
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == ':' || json[idx] == '\t'))
            idx++;
        return idx < json.Length && json.Substring(idx).StartsWith("true");
    }
}
}
