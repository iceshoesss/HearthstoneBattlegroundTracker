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
    private static string _baseUrl = "https://www.dazy.asia/";
    public static string BaseUrl => _baseUrl;

    // ── 插件鉴权 Key（Release 编译时写入） ──
    // 这是一把「共享凭证」而非秘密：它同时存在于 git 历史和发布的 exe 里，字符串一扫就能拿到。
    // 因此服务端必须按低信任凭证处理：
    //   - 只授予 /api/plugin/* 的上报权限，绝不给读管理数据 / 改配置的能力；
    //   - 必须叠加 accountIdLo 身份校验 + 频控，不能只凭这把 Key 放行；
    //   - 轮换：两端同时改，旧 Key 保留一个版本过渡期后下线。
    // 若将来需要真正保密的凭证，应改为服务端下发短期 token，而不是继续往这里加常量。
#if DEBUG
    private const string ApiKey = "";
#else
    private const string ApiKey = "wangbadanandeluoni123";
#endif

    /// <summary>
    /// 统一的接口结果。HTTP 与 WS 两条通道都归一到这个结构。
    ///
    /// 背景：服务端 WS 应答形如 {ok:false, error:'...', code:'placement_taken'}，
    /// 旧代码只判断"有没有收到回复"，于是 409/400/429 全被记成成功——这是丢数据的根因。
    /// </summary>
    public sealed class ApiResult
    {
        /// <summary>HTTP 状态码；0 表示网络异常 / 未收到应答</summary>
        public int Status;
        /// <summary>原始响应体</summary>
        public string Json = "";
        public string Error = "";
        /// <summary>业务错误码，如 placement_taken</summary>
        public string Code = "";
        /// <summary>服务端是否标记为可重试</summary>
        public bool Retryable;

        public bool Ok => Status >= 200 && Status < 300;
        public bool IsNetworkError => Status == 0;
        /// <summary>目标名次被占：服务端明确要求「稍后重试」</summary>
        public bool PlacementTaken => Code == "placement_taken";
        /// <summary>本人名次已记录（409 且不是 placement_taken）→ 数据已在库里，按成功处理</summary>
        public bool AlreadyRecorded => Status == 409 && !PlacementTaken;

        public override string ToString()
        {
            if (Ok) return "OK";
            var sb = new StringBuilder();
            sb.Append(IsNetworkError ? "网络异常" : Status.ToString());
            if (!string.IsNullOrEmpty(Code)) sb.Append(' ').Append(Code);
            if (!string.IsNullOrEmpty(Error)) sb.Append(" · ").Append(Error);
            return sb.ToString();
        }
    }

    // X-HDT-Plugin：读程序集版本（csproj Version），与联赛工具窗口标题一致
    private static readonly string _pluginVersion = GetAssemblyVersion();

    private static string GetAssemblyVersion()
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v == null) return "0.7.0";
            return $"{v.Major}.{v.Minor}.{(v.Build < 0 ? 0 : v.Build)}";
        }
        catch
        {
            return "0.7.0";
        }
    }

    static ApiClient()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    }

    private static readonly HttpClient _http = new HttpClient(
        new HttpClientHandler { UseProxy = false }
    ) { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>直连失败时的兜底：走系统代理（部分选手开了代理/VPN，直连不通）</summary>
    private static readonly HttpClient _httpProxied = new HttpClient(
        new HttpClientHandler { UseProxy = true }
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
            var res = await SendWithWsFallback("remap-los", "/api/plugin/test/remap-los", body, 8000);
            if (!res.Ok)
            {
                Log("[API] test remap-los 失败: " + res);
                return false;
            }
            Log("[API] test remap-los 完成: " + res.Json);
            return true;
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
            // 返回 null 而不是 false：这是「暂时读不到」，不是「确认非联赛局」，要让调用方继续重试
            return null;
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
            var res = await SendWithWsFallback("check-league", "/api/plugin/check-league", body, 10000);
            if (!res.Ok)
            {
                // 关键：任何失败都返回 null（=「结果未知，可重试」），绝不能返回 false。
                // false 在调用方语义是「确认这不是联赛局」，会把一次 429/5xx 变成整局不上报。
                LastError = res.ToString();
                Console.WriteLine($"[API] ⚠️ check-league 失败（可重试）: {res}");
                return null;
            }
            var json = res.Json;

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
            var res = await PostAsync("/api/plugin/initialize-player", body);
            if (!res.Ok)
            {
                LastError = $"initialize-player {res}";
                return false;
            }

            var vc = ExtractJsonString(res.Json, "verificationCode");
            if (!string.IsNullOrEmpty(vc))
            {
                VerificationCode = vc;
                Console.WriteLine("[API] ✅ 验证码: " + VerificationCode);
                return true;
            }
            else
            {
                Console.WriteLine("[API] ⚠️ initialize-player 响应中无 verificationCode，json=" + res.Json);
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
    public static async Task<ApiResult> UpdatePlacementAsync(
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
        // 每局拔线次数（服务端显示 +9(-N)）；用本局 ReconnectTimes，不跨局累计
        var disc = reconnectTimes == null ? 0 : reconnectTimes.Count;
        body["disconnects"] = disc;
        if (reconnectTimes != null && reconnectTimes.Count > 0)
            body["reconnectTimes"] = reconnectTimes;
        if (otherPlacements != null && otherPlacements.Count > 0)
        {
            var opList = new List<object>();
            foreach (var (lo, p) in otherPlacements)
                opList.Add(new Dictionary<string, object> { ["accountIdLo"] = lo.ToString(), ["placement"] = p });
            body["otherPlacements"] = opList;
        }

        // 单次尝试（WS 优先 / HTTP 兜底）；重试、退避、持久化都在 UploadQueue 里做
        var res = await SendWithWsFallback("update-placement", "/api/plugin/update-placement", body, 12000);
        if (res.Ok)
        {
            var written = ExtractJsonString(res.Json, "written");
            var kept = ExtractJsonString(res.Json, "kept");
            var skipped = ExtractJsonString(res.Json, "skipped");
            Console.WriteLine($"[API] ✅ 排名已上传: 第 {placement} 名 | finalized={ExtractJsonBool(res.Json, "finalized")} written={written} kept={kept} skipped={skipped}");
            // 200 但一行没写：多半是 accountIdLo 不在本局 roster 里，别把它当成成功
            var wroteNothing = (string.IsNullOrEmpty(written) || written == "0")
                               && (string.IsNullOrEmpty(kept) || kept == "0");
            if (wroteNothing)
                Console.WriteLine("[API] ⚠️ 服务端 200 但未写入任何行（accountIdLo 可能不在本局名单内），请核对 roster");
        }
        else
        {
            LastError = $"update-placement {res}";
            Console.WriteLine($"[API] ⚠️ update-placement 未写入: {res}");
        }
        return res;
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
            var res = await SendWithWsFallback("check-league", "/api/plugin/check-league", body, 10000);
            if (!res.Ok)
            {
                // 与 BG 版本一致：失败一律返回 null（可重试），不要把错误当成「非联赛局」
                LastError = res.ToString();
                Console.WriteLine($"[API] ⚠️ check-league(constructed) 失败（可重试）: {res}");
                return null;
            }
            var json = res.Json;

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

        var res = await SendWithWsFallback("update-placement", "/api/plugin/update-placement", body, 12000);
        if (res.Ok)
        {
            Console.WriteLine($"[API] ✅ 双方结果已上传: 本机={localPlacement} 对手={oppPlacement}");
            return true;
        }
        LastError = $"update-placement(constructed) {res}";
        Console.WriteLine($"[API] ⚠️ {LastError}");
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
            var res = await PostAsync("/api/plugin/report-game-stats", body);
            if (!res.Ok)
            {
                LastError = $"report-game-stats {res}";
                return false;
            }
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
        var res = await PostAsync("/api/plugin/queue/join", body);
        if (!res.Ok) { LastError = res.ToString(); return new QueueJoinResult(); }
        return new QueueJoinResult
        {
            Ticket = ExtractJsonString(res.Json, "ticket"),
            State = ExtractJsonString(res.Json, "state"),
            TableNamesJson = ExtractJsonArray(res.Json, "tableNames"),
        };
    }

    public static async Task<bool> QueueLeaveAsync(string ticket)
    {
        var body = new Dictionary<string, object> { { "ticket", ticket } };
        var res = await PostAsync("/api/plugin/queue/leave", body);
        if (!res.Ok) LastError = res.ToString();
        return res.Ok;
    }

    public static async Task<bool> ConfirmPlacementAsync(string ticket, int placement, bool okConfirm)
    {
        var body = new Dictionary<string, object>
        {
            { "ticket", ticket },
            { "placement", placement },
            { "ok", okConfirm },
        };
        var res = await PostAsync("/api/plugin/confirm-placement", body);
        if (!res.Ok) LastError = res.ToString();
        return res.Ok;
    }

    // ── HTTP 工具方法 ──

    private static async Task<ApiResult> PostAsync(string path, Dictionary<string, object> body)
    {
        var jsonBody = SimpleJsonSerialize(body);
        Console.WriteLine($"[API] → {path} body={jsonBody}");

        var res = await PostOnce(path, jsonBody, _http);
        if (!res.IsNetworkError) return res;

        // 直连挂了再试系统代理：有的选手挂 VPN/代理，直连必然失败
        var proxied = await PostOnce(path, jsonBody, _httpProxied);
        if (!proxied.IsNetworkError)
        {
            Console.WriteLine($"[API] ℹ️ {path} 直连失败，经系统代理成功");
            return proxied;
        }
        return res;
    }

    private static async Task<ApiResult> PostOnce(string path, string jsonBody, HttpClient client)
    {
        var res = new ApiResult();
        try
        {
            using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path) { Content = content })
            {
                request.Headers.Add("X-HDT-Plugin", _pluginVersion);
                if (!string.IsNullOrEmpty(ApiKey))
                    request.Headers.Add("Authorization", $"Bearer {ApiKey}");

                var response = await client.SendAsync(request);
                var respBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ← {path} {(int)response.StatusCode} {respBody}");
                FillFromBody(res, (int)response.StatusCode, respBody);
                if (!res.Ok) Console.WriteLine($"[API] ❌ {path} → {res}");
                return res;
            }
        }
        catch (Exception e)
        {
            // 超时 / DNS / TLS / 拒连：统一按可重试的网络错误处理，不再让异常冒到调用方
            res.Status = 0;
            res.Error = e.GetType().Name + ": " + e.Message;
            res.Retryable = true;
            Console.WriteLine($"[API] ❌ {path} 网络异常: {res.Error}");
            return res;
        }
    }

    /// <summary>把响应体里的业务字段填进 ApiResult（HTTP 与 WS 共用）</summary>
    private static void FillFromBody(ApiResult res, int status, string body)
    {
        res.Status = status;
        res.Json = body ?? "";
        if (res.Ok) return;
        res.Error = ExtractJsonString(res.Json, "error");
        res.Code = ExtractJsonString(res.Json, "code");
        res.Retryable = ExtractJsonBool(res.Json, "retryable")
                        || status == 0 || status == 429 || status >= 500;
    }

    /// <summary>
    /// WS 请求-应答的回复体 → ApiResult。
    /// 服务端回复形如 {type:"update-placement_result", reqId, ok:true/false, error, code, ...业务字段}。
    /// </summary>
    private static ApiResult FromWsReply(string json)
    {
        var res = new ApiResult();
        if (string.IsNullOrEmpty(json))
        {
            res.Status = 0;
            res.Error = "WS 无应答";
            res.Retryable = true;
            return res;
        }
        res.Json = json;
        res.Code = ExtractJsonString(json, "code");
        res.Error = ExtractJsonString(json, "error");

        // 服务端若带 status 就直接采信；否则按 ok 推断（200 / 400）
        int status;
        if (!int.TryParse(ExtractJsonString(json, "status"), out status) || status <= 0)
            status = ExtractJsonBool(json, "ok") ? 200 : 400;

        // ok:false 且没有 status 时，用错误码/文案还原真实语义。
        // 关键取舍：认不出来的错误按 503（可重试）处理——宁可多试几次，
        // 也不能把一个服务端 5xx 误判成 400 把数据丢掉。
        if (status == 400)
        {
            if (res.Code == "placement_taken") status = 409;
            else if (!string.IsNullOrEmpty(res.Error) && res.Error.Contains("已提交")) status = 409;
            else if (!string.IsNullOrEmpty(res.Error) && res.Error.Contains("过于频繁")) status = 429;
            else if (!string.IsNullOrEmpty(res.Error)
                     && (res.Error.Contains("1-8") || res.Error.Contains("对局不存在") || res.Error.Contains("gameUuid")))
                status = 400;   // 明确的不可恢复错误
            else
                status = 503;   // 未知错误：按可重试处理
        }

        FillFromBody(res, status, json);
        return res;
    }

    /// <summary>
    /// 单次请求：WS 优先，无应答时退回 HTTP。
    /// 注意「WS 有应答但 ok:false」不会退回 HTTP——那是对端明确的业务结论，重复发只会制造 409。
    /// 重试由调用方（UploadQueue）按 ApiResult 语义处理。
    /// </summary>
    private static async Task<ApiResult> SendWithWsFallback(
        string wsType, string httpPath, Dictionary<string, object> body, int wsTimeoutMs)
    {
        var ws = MatchSessionClient.Active;
        if (ws != null && ws.IsConnected)
        {
            var reply = await ws.RequestAsync(wsType, body, wsTimeoutMs);
            if (reply != null)
            {
                var r = FromWsReply(reply);
                Console.WriteLine($"[API] {wsType} WS → {r}");
                return r;
            }
            Console.WriteLine($"[API] {wsType} WS 超时无应答，退回 HTTP");
        }

        var http = await PostAsync(httpPath, body);
        Console.WriteLine($"[API] {wsType} HTTP → {http}");
        return http;
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
