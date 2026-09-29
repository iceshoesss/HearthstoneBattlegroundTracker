using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HBT
{
/// <summary>
/// 一场一连：SSE 事件流。ticket 在 queue/join 时发放。
/// 支持短暂断线重连；Stop/Dispose 会取消并丢弃过期连接。
/// </summary>
public class MatchSessionClient : IDisposable
{
    private readonly string _baseUrl;
    private readonly string _ticket;
    private CancellationTokenSource _cts;
    private Task _loop;
    private int _generation;

    public Action<string> OnEventJson { get; set; }
    public Action OnClosed { get; set; }

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
                using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }))
                {
                    http.Timeout = Timeout.InfiniteTimeSpan;
                    var url = _baseUrl + "/api/plugin/match/events?ticket=" + Uri.EscapeDataString(_ticket);
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.Add("X-Match-Ticket", _ticket);
                        request.Headers.Add("X-HDT-Plugin", "0.7.0");
                        using (var resp = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                        using (var stream = await resp.Content.ReadAsStreamAsync())
                        using (var reader = new StreamReader(stream))
                        {
                            fail = 0;
                            while (!ct.IsCancellationRequested)
                            {
                                var line = await ReadLineWithTimeoutAsync(reader, ct, 180000);
                                if (line == null) break;
                                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                                var payload = line.Substring(5).Trim();
                                if (payload.Length == 0) continue;
                                if (payload.Contains("\"type\":\"ping\"")) continue;
                                if (_generation != generation) return;
                                OnEventJson?.Invoke(payload);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested || _generation != generation) return;
                fail++;
                Console.WriteLine("[MATCH] SSE 中断(" + fail + "): " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * fail)), ct); }
                catch { return; }
                if (_generation != generation) return;
                continue;
            }
            break;
        }

        if (!ct.IsCancellationRequested && _generation == generation)
            OnClosed?.Invoke();
    }

    private static async Task<string> ReadLineWithTimeoutAsync(StreamReader reader, CancellationToken ct, int timeoutMs)
    {
        var task = reader.ReadLineAsync();
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, ct));
        if (completed != task)
        {
            throw new TimeoutException("SSE read timeout");
        }
        return await task;
    }

    public void Dispose() { Stop(); }
}
}
