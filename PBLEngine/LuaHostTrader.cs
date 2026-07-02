using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PBLEngine;

// Trader-часть LuaHost: HTTP-мост для launch:DownloadPage и обвязка PBLTrader.
// Lua-классы трейдера (TradeQueryGenerator/-Requests/-RateLimiter) не меняются —
// им нужен только рабочий launch:DownloadPage, которого нет в HeadlessWrapper.
public sealed partial class LuaHost
{
    private sealed record HttpCompletion(long Id, string? Body, string? Header, string? Error);

    private readonly ConcurrentQueue<HttpCompletion> _httpCompletions = new();
    private int _httpInFlight;
    private HttpClient? _traderHttpClient;
    private HttpMessageHandler? _activeHttpHandler;
    private bool _traderHttpRegistered;

    /// <summary>Подменный handler для тестов; null → реальный SocketsHttpHandler.
    /// Учитывается при каждом EnsureTraderHttp (клиент пересоздаётся при смене).</summary>
    public HttpMessageHandler? TraderHttpHandler { get; set; }

    /// <summary>true когда нет HTTP-запросов в полёте (готовые ответы могут ждать в очереди).</summary>
    public bool TraderHttpIdle => Volatile.Read(ref _httpInFlight) == 0;

    private static string EngineLuaDir => Path.Combine(AppContext.BaseDirectory, "lua");

    public void EnsureTraderHttp()
    {
        if (!_traderHttpRegistered)
        {
            State.RegisterFunction("PBL_HttpStart", this,
                typeof(LuaHost).GetMethod(nameof(TraderHttpStart),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
            var luaPath = Path.Combine(EngineLuaDir, "trader_http.lua");
            State.DoString(File.ReadAllText(luaPath), "@trader_http.lua");
            _traderHttpRegistered = true;
        }
        if (_traderHttpClient is null || !ReferenceEquals(_activeHttpHandler, TraderHttpHandler))
        {
            _traderHttpClient?.Dispose();
            _activeHttpHandler = TraderHttpHandler;
            // Инжектированный handler не диспозим вместе с клиентом — им владеет тест
            _traderHttpClient = TraderHttpHandler is null
                ? new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
                : new HttpClient(TraderHttpHandler, disposeHandler: false);
            _traderHttpClient.Timeout = TimeSpan.FromSeconds(30);
            _traderHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PathOfBuilding-PBLApp/1.0");
        }
    }

    private bool _traderInitLoaded;

    /// <summary>Идемпотентно: HTTP-мост + загрузка trader.lua + PBLTrader.Init()
    /// (пере-инициализация генератора при смене билда — внутри Init).</summary>
    public void EnsureTraderInit()
    {
        EnsureTraderHttp();
        if (!_traderInitLoaded)
        {
            State.DoString(File.ReadAllText(Path.Combine(EngineLuaDir, "trader.lua")), "@trader.lua");
            _traderInitLoaded = true;
        }
        State.DoString("PBLTrader.Init()");
    }

    // Вызывается ИЗ Lua (на Lua-потоке). Снимает данные и уходит в пул — Lua не блокируется.
    private void TraderHttpStart(long id, string url, string? header, string? body)
    {
        var client = _traderHttpClient!;
        Interlocked.Increment(ref _httpInFlight);
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(
                    body is null ? HttpMethod.Get : HttpMethod.Post, url);
                if (body is not null)
                    req.Content = new StringContent(body);
                if (header is not null)
                {
                    foreach (var line in header.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var idx = line.IndexOf(':');
                        if (idx <= 0) continue;
                        var name = line[..idx].Trim();
                        var value = line[(idx + 1)..].Trim();
                        if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            if (req.Content is not null)
                                req.Content.Headers.ContentType =
                                    new System.Net.Http.Headers.MediaTypeHeaderValue(value);
                        }
                        else
                        {
                            req.Headers.TryAddWithoutValidation(name, value);
                        }
                    }
                }
                using var resp = await client.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();
                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {(int)resp.StatusCode} {resp.ReasonPhrase}\r\n");
                foreach (var h in resp.Headers)
                    foreach (var v in h.Value) sb.Append($"{h.Key}: {v}\r\n");
                foreach (var h in resp.Content.Headers)
                    foreach (var v in h.Value) sb.Append($"{h.Key}: {v}\r\n");
                // Не-2xx отдаём как errMsg + header: коды 429/401 разбирает сам Lua-код трейдера
                string? err = resp.IsSuccessStatusCode
                    ? null : $"Response code: {(int)resp.StatusCode}";
                _httpCompletions.Enqueue(new HttpCompletion(id, respBody, sb.ToString(), err));
            }
            catch (Exception ex)
            {
                _httpCompletions.Enqueue(new HttpCompletion(id, null, null, ex.Message));
            }
            finally { Interlocked.Decrement(ref _httpInFlight); }
        });
    }

    /// <summary>Доставить готовые HTTP-ответы Lua-callback'ам.
    /// Вызывать только с потока, владеющего Lua-состоянием (или под trader-семафором).</summary>
    public int DrainTraderHttp()
    {
        var n = 0;
        while (_httpCompletions.TryDequeue(out var c))
        {
            State["_pblHttpBody"] = c.Body;
            State["_pblHttpHeader"] = c.Header;
            State["_pblHttpErr"] = c.Error;
            State.DoString($"_pblHttpComplete({c.Id}, _pblHttpBody, _pblHttpHeader, _pblHttpErr)");
            State["_pblHttpBody"] = null;
            State["_pblHttpHeader"] = null;
            State["_pblHttpErr"] = null;
            n++;
        }
        return n;
    }
}
