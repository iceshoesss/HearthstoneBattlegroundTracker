using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HBT
{
/// <summary>
/// 一场一连：WebSocket 事件流。ticket 在 queue/join 时发放。
/// 支持短暂断线重连；Stop/Dispose 会取消并丢弃过期连接。
/// </summary>
public class MatchSessionClient : IDisposable
{
    private readonly string _baseUrl;
    private readonly string _ticket;
    private CancellationTokenSource _cts;
    private Task _loop;
    private int _generation;
    private ClientWebSocket _ws;
    private readonly object _sendLock = new object();
    private int _reqSeq;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending =
        new ConcurrentDictionary<string, TaskCompletionSource<string>>();

    /// <summary>当前活跃的 match WS（供 ApiClient 优先走 WS）</summary>
    public static MatchSessionClient Active;

    public Action<string> OnEventJson { get; set; }
    /// <summary>WebSocket 已建立（用于连接指示灯）</summary>
    public Action OnOpen { get; set; }
    /// <summary>连接结束/失败（用于连接指示灯）</summary>
    public Action OnClosed { get; set; }

    /// <summary>当前是否连着 WS</summary>
    public bool IsConnected { get; private set; }

    /// <summary>向服务器发一条 JSON（如 in_room 状态）；未连接返回 false</summary>
    public bool SendJson(string json)
    {
        try
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return false;
            var buf = Encoding.UTF8.GetBytes(json ?? "{}");
            lock (_sendLock)
            {
                ws.SendAsync(new ArraySegment<byte>(buf), WebSocketMessageType.Text, true, CancellationToken.None)
                    .Wait(2000);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 请求-应答：往 payload 注入 reqId，等待同 reqId 的 *_result。
    /// 超时或未连接返回 null（调用方可退回 HTTP）。
    /// </summary>
    public async Task<string> RequestAsync(string type, object payload, int timeoutMs = 12000)
    {
        if (!IsConnected) return null;
        var reqId = Interlocked.Increment(ref _reqSeq).ToString();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[reqId] = tcs;
        try
        {
            string json;
            if (payload is string s)
            {
                // 已是 JSON 对象串
                json = s.TrimEnd().TrimEnd('}');
                if (json.Length > 0) json += ",";
                json += "\"type\":\"" + type + "\",\"reqId\":\"" + reqId + "\"}";
            }
            else
            {
                // 简单 KV 序列化（够用，避免引 Newtonsoft 到此处）
                var sb = new StringBuilder();
                sb.Append("{\"type\":\"").Append(type).Append("\",\"reqId\":\"").Append(reqId).Append("\"");
                if (payload is System.Collections.Generic.Dictionary<string, object> dict)
                {
                    foreach (var kv in dict)
                    {
                        sb.Append(",\"").Append(kv.Key).Append("\":");
                        AppendJsonValue(sb, kv.Value);
                    }
                }
                sb.Append('}');
                json = sb.ToString();
            }
            if (!SendJson(json)) return null;
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (done != tcs.Task) return null;
            return await tcs.Task;
        }
        finally
        {
            TaskCompletionSource<string> _;
            _pending.TryRemove(reqId, out _);
        }
    }

    private static void AppendJsonValue(StringBuilder sb, object v)
    {
        if (v == null) { sb.Append("null"); return; }
        if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
        if (v is int || v is long || v is double || v is float || v is decimal) { sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)); return; }
        var sd = v as System.Collections.Generic.IDictionary<string, object>;
        if (sd != null)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in sd)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":");
                AppendJsonValue(sb, kv.Value);
            }
            sb.Append('}');
            return;
        }
        if (v is System.Collections.IEnumerable en && !(v is string))
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in en)
            {
                if (!first) sb.Append(',');
                first = false;
                AppendJsonValue(sb, item);
            }
            sb.Append(']');
            return;
        }
        var str = Convert.ToString(v) ?? "";
        sb.Append('"').Append(str.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
    }

    private void TryCompletePending(string json)
    {
        try
        {
            const string key = "\"reqId\":";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return;
            var start = idx + key.Length;
            var quote = json.IndexOf('"', start);
            if (quote < 0) return;
            var end = json.IndexOf('"', quote + 1);
            if (end < 0) return;
            var reqId = json.Substring(quote + 1, end - quote - 1);
            TaskCompletionSource<string> tcs;
            if (_pending.TryGetValue(reqId, out tcs))
            {
                tcs.TrySetResult(json);
            }
        }
        catch { /* ignore */ }
    }

    public MatchSessionClient(string baseUrl, string ticket)
    {
        _baseUrl = (baseUrl ?? "").TrimEnd('/');
        _ticket = ticket ?? "";
    }

    public void Start()
    {
        Stop();
        Active = this;
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        _loop = Task.Run(() => RunAsync(_cts.Token, gen));
    }

    public void Stop()
    {
        IsConnected = false;
        _ws = null;
        if (ReferenceEquals(Active, this)) Active = null;
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        var loop = _loop;
        _loop = null;
        if (loop != null)
        {
            try { loop.Wait(500); } catch { }
        }
    }

    private async Task RunAsync(CancellationToken ct, int generation)
    {
        int fail = 0;
        while (!ct.IsCancellationRequested && fail < 5)
        {
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    var uri = BuildWsUri();
                    Console.WriteLine("[MATCH] WS 连接 " + uri);
                    await ws.ConnectAsync(uri, ct);
                    if (_generation != generation) return;
                    _ws = ws;
                    IsConnected = true;
                    OnOpen?.Invoke();

                    var buf = new byte[16 * 1024];
                    var sb = new StringBuilder();
                    fail = 0;
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        sb.Length = 0;
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                                break;
                            }
                            sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close) break;
                        if (_generation != generation) return;

                        var payload = sb.ToString().Trim();
                        if (payload.Length == 0) continue;
                        if (payload.Contains("\"type\":\"pong\"")) continue;
                        if (payload.Contains("\"type\":\"ping\"")) continue;
                        // 请求-应答优先
                        if (payload.Contains("_result\""))
                        {
                            TryCompletePending(payload);
                            continue;
                        }
                        OnEventJson?.Invoke(payload);
                    }
                }
            }
            catch (Exception ex)
            {
                _ws = null;
                IsConnected = false;
                if (ct.IsCancellationRequested || _generation != generation) return;
                fail++;
                Console.WriteLine("[MATCH] WS 中断(" + fail + "): " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * fail)), ct); }
                catch { IsConnected = false; return; }
                if (_generation != generation) return;
                continue;
            }
            _ws = null;
            IsConnected = false;
            break;
        }

        IsConnected = false;
        if (!ct.IsCancellationRequested && _generation == generation)
            OnClosed?.Invoke();
    }

    private Uri BuildWsUri()
    {
        var http = _baseUrl;
        string wsBase;
        if (http.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            wsBase = "wss://" + http.Substring("https://".Length);
        else if (http.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            wsBase = "ws://" + http.Substring("http://".Length);
        else
            wsBase = "ws://" + http;
        return new Uri(wsBase.TrimEnd('/') + "/api/plugin/match/ws?ticket=" + Uri.EscapeDataString(_ticket));
    }

    public void Dispose() { Stop(); }
}
}
