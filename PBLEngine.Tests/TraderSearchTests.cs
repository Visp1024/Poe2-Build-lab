using PBLEngine;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderSearchTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderSearchTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    // Запрос как строит генератор: weight-стата обязательна — при total<10
    // SearchWithQueryWeightAdjusted половинит query.stats[1].value.min и повторяет
    private const string WeightQuery =
        """{"query":{"status":{"option":"online"},"stats":[{"type":"weight","value":{"min":100},"filters":[{"id":"explicit.stat_1","value":{"weight":10}}]}]},"sort":{"statgroup.0":"desc"},"engine":"new"}""";

    // Минимальный search-ответ: 2 результата
    private const string SearchJson =
        """{"id":"testquery1","complexity":10,"result":["hashA","hashB"],"total":2}""";

    // Fetch-ответ в форме, которую разбирает FetchResultBlock (TradeQueryRequests.lua:272-444):
    // explicitMods — объекты {description, flags}; listing.price/whisper/account обязательны.
    private const string FetchJson =
        """
        {"result":[
          {"id":"hashA",
           "item":{"rarity":"RARE","name":"Doom Crown","typeLine":"Advanced Warrior Greathelm","ilvl":81,
                   "properties":[{"name":"Armour","values":[["500",0]]}],
                   "requirements":[{"name":"Level","values":[["65",0]]}],
                   "explicitMods":[{"description":"+120 to maximum Life","flags":{}}],
                   "pseudoMods":["Sum: 123.4"]},
           "listing":{"price":{"amount":5,"currency":"divine","type":"buyout"},
                      "whisper":"@Seller1 Hi, I would like to buy your Doom Crown",
                      "account":{"name":"Seller1"}}},
          {"id":"hashB",
           "item":{"rarity":"RARE","name":"Grim Visor","typeLine":"Advanced Warrior Greathelm","ilvl":80,
                   "explicitMods":[{"description":"+90 to maximum Life","flags":{}}]},
           "listing":{"price":{"amount":2,"currency":"exalted","type":"buyout"},
                      "whisper":"@Seller2 Hi, I would like to buy your Grim Visor",
                      "account":{"name":"Seller2"}}}
        ]}
        """;

    private FakeHttpHandler Setup()
    {
        var fake = new FakeHttpHandler();
        fake.Responder = req =>
        {
            var url = req.RequestUri!.ToString();
            var json = url.Contains("/api/trade2/search/") ? SearchJson
                     : url.Contains("/api/trade2/fetch/") ? FetchJson
                     : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        };
        _host.TraderHttpHandler = fake;
        return fake;
    }

    [Fact(Timeout = 30_000)]
    public async Task SearchTrade_ParsesListings()
    {
        Setup();
        var result = await _host.SearchTradeAsync("Standard", WeightQuery, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("testquery1", result.QueryId);
        Assert.Equal(2, result.Listings.Count);
        var first = result.Listings[0];
        Assert.Equal(5, first.Amount);
        Assert.Equal("divine", first.Currency);
        Assert.Equal("Seller1", first.Seller);
        Assert.StartsWith("@Seller1", first.Whisper);
        Assert.Contains("Doom Crown", first.ItemText);
        Assert.Contains("+120 to maximum Life", first.ItemText);
    }

    [Fact(Timeout = 30_000)]
    public async Task SearchTrade_WithAuthToken_SendsBearer()
    {
        var fake = Setup();
        _host.EnsureTraderInit();
        _host.State.DoString("main.api.authToken = 'tok-abc'");
        try
        {
            await _host.SearchTradeAsync("Standard", WeightQuery, CancellationToken.None);
            Assert.Contains(fake.Requests,
                r => r.Headers.Authorization?.ToString() == "Bearer tok-abc");
        }
        finally
        {
            _host.State.DoString("main.api.authToken = nil");
        }
    }
}
