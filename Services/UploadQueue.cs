using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HBT
{
/// <summary>
/// 排名上报的持久化队列（outbox）。
///
/// 解决的问题：此前 update-placement 在日志线程上同步跑 3 次 ×2s，失败即丢；
/// 选手打完就关程序、服务端瞬时 5xx、限流窗口都会让名次永久缺失，只能靠别人兜底。
///
/// 现在的语义：
/// - 对局结束只做入队（内存 + 落盘），立刻返回，不阻塞状态机
/// - 后台单线程按退避重试：429 按限流窗口等、409 名次被占延后重试、5xx/网络抖动指数退避
/// - 只有「服务端确认写入」或「服务端说该玩家已有名次」才出队
/// - 400 这类不可恢复错误立即出队并打日志，不浪费全桌共享的限流额度
/// - 进程重启后从 upload_queue.json 续传，超过 24 小时的条目自动丢弃
/// </summary>
public static class UploadQueue
{
    public class OtherPlacement
    {
        public ulong Lo;
        public int Placement;
    }

    public class Item
    {
        public string GameUuid = "";
        public string BattleTag = "";
        public ulong AccountIdLo;
        /// <summary>0 = 名次还没读到，交由 PlacementResolver 补齐</summary>
        public int Placement;
        public List<string> ReconnectTimes = new List<string>();
        public List<OtherPlacement> Others = new List<OtherPlacement>();
        /// <summary>true = 兜底上报（服务端按 timeout 记 0 分）</summary>
        public bool Timeout;
        public string CreatedAt = DateTime.UtcNow.ToString("o");
        public int Attempts;
        public string LastError = "";
        /// <summary>下次可尝试时间（UTC ticks）</summary>
        public long NextAttemptTicks;
    }

    /// <summary>名次未知时的补齐回调（读内存排名）。入参是 gameUuid，返回 0 表示读不到。</summary>
    public static Func<string, int> PlacementResolver;
    public static Action<string> OnLog;

    // 退避（毫秒）：首次立刻，之后 1s/3s/8s/20s/30s/60s…
    private static readonly int[] BackoffMs = { 0, 1000, 3000, 8000, 20000, 30000, 60000, 60000, 60000, 60000 };
    /// <summary>单次运行内的尝试上限；超出后留到下次启动继续</summary>
    private const int MaxAttemptsPerSession = 10;
    private const int MaxUnknownPlacementAttempts = 12;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static readonly object _lock = new object();
    private static readonly List<Item> _items = new List<Item>();
    private static string _path = "";
    private static CancellationTokenSource _cts;
    private static Task _worker;

    public static int PendingCount
    {
        get { lock (_lock) return _items.Count; }
    }

    public static void Init(string dir = null)
    {
        var baseDir = dir ?? AppDomain.CurrentDomain.BaseDirectory;
        _path = Path.Combine(baseDir, "upload_queue.json");
        Load();
        StartWorker();
    }

    /// <summary>入队（同一 gameUuid 只保留一条；已有的不覆盖，避免重复上报）</summary>
    public static void Enqueue(Item item)
    {
        if (item == null || string.IsNullOrEmpty(item.GameUuid)) return;
        lock (_lock)
        {
            if (_items.Any(i => i.GameUuid == item.GameUuid))
            {
                Log($"[Upload] 该局已在队列中，跳过重复入队: {item.GameUuid}");
                return;
            }
            item.NextAttemptTicks = 0;
            _items.Add(item);
            Save();
            Log($"[Upload] 入队 {item.GameUuid} 名次={item.Placement} 兜底={item.Timeout} 队列={_items.Count}");
        }
        Kick();
    }

    /// <summary>关窗时调用：给在途上报最后一点时间（最多 maxMs 毫秒）</summary>
    public static void FlushBlocking(int maxMs)
    {
        if (PendingCount == 0) return;
        Log($"[Upload] 退出前等待 {PendingCount} 条上报（最多 {maxMs}ms）");
        lock (_lock)
        {
            foreach (var i in _items) i.NextAttemptTicks = 0;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (PendingCount > 0 && sw.ElapsedMilliseconds < maxMs)
            Thread.Sleep(50);
        Log($"[Upload] 退出时剩余 {PendingCount} 条未确认（已落盘，下次启动续传）");
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); } catch { }
    }

    // ═══════════════════════════════════════
    //  内部
    // ═══════════════════════════════════════

    private static void StartWorker()
    {
        if (_worker != null) return;
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    private static void Kick()
    {
        // 只保证工作线程在跑；不动其他条目的退避时间，避免一次入队把全队列的退避打乱
        StartWorker();
    }

    private static async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Item item = null;
            lock (_lock)
            {
                var now = DateTime.UtcNow.Ticks;
                item = _items.FirstOrDefault(i => i.NextAttemptTicks <= now);
            }

            if (item == null)
            {
                try { await Task.Delay(800, ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await TrySendAsync(item, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"[Upload] 处理异常: {ex.Message}");
                lock (_lock)
                {
                    item.Attempts++;
                    item.LastError = ex.Message;
                    item.NextAttemptTicks = DateTime.UtcNow.AddSeconds(10).Ticks;
                    Save();
                }
            }
        }
    }

    private static async Task TrySendAsync(Item item, CancellationToken ct)
    {
        // 1) 名次未知：先请 GameMonitorService 从内存排名补；补不到就等一会儿再试
        if (item.Placement <= 0)
            ResolvePlacement(item);

        if (item.Placement <= 0)
        {
            lock (_lock)
            {
                item.Attempts++;
                item.LastError = "名次尚未读到";
                if (item.Attempts >= MaxUnknownPlacementAttempts)
                {
                    Drop(item, "名次始终未读到，交给兜底/服务端自动推算");
                }
                else
                {
                    item.NextAttemptTicks = DateTime.UtcNow.AddSeconds(3).Ticks;
                    Save();
                }
            }
            return;
        }

        if (ct.IsCancellationRequested) return;

        // 2) 单次上报（WS 优先 / HTTP 兜底）
        var others = item.Others?.Select(o => (o.Lo, o.Placement)).ToList()
                     ?? new List<(ulong, int)>();
        var res = await ApiClient.UpdatePlacementAsync(
            item.GameUuid, item.BattleTag, item.AccountIdLo, item.Placement,
            item.ReconnectTimes, others, item.Timeout);

        lock (_lock)
        {
            if (res.Ok)
            {
                Remove(item, $"已写入 第{item.Placement}名" + (item.Timeout ? "（兜底不计分）" : ""));
                return;
            }

            // 服务端说「该玩家已有名次」→ 数据已在库里（常见于别人先兜底，或上次 WS 超时其实已写成）
            if (res.AlreadyRecorded)
            {
                Remove(item, "服务端已有该名次（含他机兜底），无需再报");
                return;
            }

            // 400 且不是名次被占：缺 gameUuid / placement 越界 / 对局不存在 —— 重试无意义
            if (res.Status == 400)
            {
                Drop(item, $"服务端拒绝且不可恢复: {res}");
                return;
            }

            item.Attempts++;
            item.LastError = res.ToString();

            if (item.Attempts >= MaxAttemptsPerSession)
            {
                // 本轮放弃，但保留条目等下次启动（游戏重启/服务端恢复后通常就能写进去）
                item.NextAttemptTicks = DateTime.UtcNow.AddHours(1).Ticks;
                Log($"[Upload] 本轮放弃，留待下次启动: {item.GameUuid} {item.LastError}");
                Save();
                return;
            }

            var delay = DelayFor(item.Attempts, res);
            item.NextAttemptTicks = DateTime.UtcNow.AddMilliseconds(delay).Ticks;
            Log($"[Upload] 第{item.Attempts}次失败: {item.GameUuid} {item.LastError} → {delay}ms 后重试");
            Save();
        }
    }

    private static void ResolvePlacement(Item item)
    {
        var resolver = PlacementResolver;
        if (resolver == null) return;
        try
        {
            var p = resolver(item.GameUuid);
            if (p > 0)
            {
                lock (_lock)
                {
                    item.Placement = p;
                    Save();
                }
                Log($"[Upload] 名次补齐: {item.GameUuid} → 第{p}名");
            }
        }
        catch (Exception ex)
        {
            Log($"[Upload] 名次补齐失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 429 是全桌共享额度（place:gameUuid 30次/60s），必须等过一个窗口；
    /// 409 placement_taken 是「别人占了目标名次」，通常几十秒内会被改写，所以也给长退避。
    /// </summary>
    private static int DelayFor(int attempts, ApiClient.ApiResult res)
    {
        var jitter = new Random().Next(0, 800);
        if (res.Status == 429) return 60_000 + jitter;
        if (res.PlacementTaken) return 30_000 + jitter;
        var idx = Math.Min(attempts, BackoffMs.Length - 1);
        return BackoffMs[idx] + jitter;
    }

    private static void Remove(Item item, string reason)
    {
        _items.Remove(item);
        Save();
        Log($"[Upload] ✅ {item.GameUuid} {reason}（剩余 {_items.Count}）");
    }

    private static void Drop(Item item, string reason)
    {
        _items.Remove(item);
        Save();
        Log($"[Upload] ⚠️ {item.GameUuid} 放弃上报: {reason}");
    }

    private static void Load()
    {
        lock (_lock)
        {
            _items.Clear();
            try
            {
                if (!File.Exists(_path)) return;
                var text = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(text)) return;
                var loaded = JsonConvert.DeserializeObject<List<Item>>(text) ?? new List<Item>();
                var cutoff = DateTime.UtcNow - MaxAge;
                foreach (var it in loaded)
                {
                    if (it == null || string.IsNullOrEmpty(it.GameUuid)) continue;
                    DateTime created;
                    // ISO 8601 带 Z：统一换算成 UTC 再比 24h 阈值，避免时区偏差
                    if (DateTime.TryParse(it.CreatedAt, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out created)
                        && created < cutoff)
                    {
                        Log($"[Upload] 丢弃过期条目（>24h）: {it.GameUuid}");
                        continue;
                    }
                    it.NextAttemptTicks = 0;
                    _items.Add(it);
                }
                if (_items.Count > 0)
                    Log($"[Upload] 载入待续传上报 {_items.Count} 条");
            }
            catch (Exception ex)
            {
                Log($"[Upload] 队列文件读取失败（忽略并重建）: {ex.Message}");
            }
        }
    }

    private static void Save()
    {
        try
        {
            if (string.IsNullOrEmpty(_path)) return;
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_items, Formatting.Indented));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
        catch (Exception ex)
        {
            Log($"[Upload] 队列落盘失败: {ex.Message}");
        }
    }

    private static void Log(string msg)
    {
        // 有宿主日志通道就走宿主（GameMonitorService.Log 会同时写 Console 与 UI），避免重复打印
        var sink = OnLog;
        if (sink != null)
        {
            try { sink(msg); return; } catch { }
        }
        Console.WriteLine(msg);
    }
}
}
