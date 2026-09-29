using System;
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

    public MatchSessionClient(string baseUrl, string ticket)
    {
        _baseUrl = (baseUrl ?? "").TrimEnd('/');
        _ticket = ticket ?? "";
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        _loop = Task.Run(() => RunAsync(_cts.Token, gen));
    }

    public void Stop()
    {
        IsConnected = false;
        _ws = null;
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
