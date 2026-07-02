using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLApp.Core.Trader;

/// <summary>
/// Прямые (без Lua) веб-запросы вкладки «Трейдер»: список лиг PoE2 и курсы
/// валют poe.ninja (в дивинах, кэш на час на лигу). Handler инжектируется в тестах.
/// </summary>
public sealed class TraderWebApi
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, (DateTime FetchedAt, IReadOnlyDictionary<string, double> Rates)> _ratesCache = [];
    private static readonly TimeSpan RatesTtl = TimeSpan.FromHours(1);

    public TraderWebApi(HttpMessageHandler? handler = null)
    {
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PathOfBuilding-PBLApp/1.0");
    }

    /// <summary>Лиги trade2-сайта — ровно те 4 варианта, что в его дропдауне
    /// (api/leagues отдаёт общий список с PoE1-лигами, которых на трейде нет).</summary>
    public async Task<IReadOnlyList<string>> GetLeaguesAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(
            "https://www.pathofexile.com/api/trade2/data/leagues", ct);
        using var doc = JsonDocument.Parse(json);
        var leagues = new List<string>();
        if (doc.RootElement.TryGetProperty("result", out var result) &&
            result.ValueKind == JsonValueKind.Array)
        {
            foreach (var league in result.EnumerateArray())
            {
                var id = league.TryGetProperty("id", out var i) ? i.GetString() : null;
                var realm = league.TryGetProperty("realm", out var r) ? r.GetString() : "poe2";
                if (!string.IsNullOrEmpty(id) && realm == "poe2")
                    leagues.Add(id);
            }
        }
        return leagues;
    }

    /// <summary>Курсы валют лиги в дивинах: id → primaryValue
    /// (форма ответа — как в TradeQuery:PriceBuilderProcessPoENinjaResponse).</summary>
    public async Task<IReadOnlyDictionary<string, double>> GetCurrencyRatesAsync(
        string league, CancellationToken ct = default)
    {
        if (_ratesCache.TryGetValue(league, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < RatesTtl)
            return cached.Rates;

        var json = await _http.GetStringAsync(
            "https://poe.ninja/poe2/api/economy/exchange/current/overview?type=Currency&league="
                + Uri.EscapeDataString(league), ct);
        using var doc = JsonDocument.Parse(json);
        var rates = new Dictionary<string, double>();
        if (doc.RootElement.TryGetProperty("lines", out var lines) &&
            lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lines.EnumerateArray())
            {
                if (line.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } cid &&
                    line.TryGetProperty("primaryValue", out var v) &&
                    v.ValueKind == JsonValueKind.Number)
                {
                    rates[cid] = v.GetDouble();
                }
            }
        }
        _ratesCache[league] = (DateTime.UtcNow, rates);
        return rates;
    }
}
