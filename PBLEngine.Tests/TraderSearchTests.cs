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
                      "account":{"name":"Seller2"}}}
        ]}
        """;
    // hashB намеренно БЕЗ whisper: реальные ~b/o-лоты его не имеют, dkjson опускает
    // nil-ключ и парсер не должен падать (KeyNotFoundException — реальный баг).

    // Реальная форма ответа GGG для посоха (снято с api.pathofexile.com):
    // runeMods — МАССИВ СТРОК, explicitMods — объекты {description, flags},
    // имена свойств содержат [Скобочную|Разметку]. Разбор обязан принимать обе
    // формы: на смешанном ответе он падал и убивал весь поиск.
    private const string StaffFetchJson =
        """
        {"result":[
          {"id":"hashStaff",
           "item":{"rarity":"RARE","name":"Victory Weaver","typeLine":"Sinister Quarterstaff","ilvl":79,
                   "properties":[{"name":"[Quarterstaff]","values":[]},
                                 {"name":"[Physical] Damage","values":[["133-219",1]]},
                                 {"name":"[Quality]","values":[["+20%",1]]}],
                   "requirements":[{"name":"Level","values":[["67",0]]}],
                   "sockets":[{"group":0,"type":"rune"},{"group":1,"type":"rune"}],
                   "runeMods":["Adds 18 to 30 [Cold|Cold] Damage",
                               "[ShamanOnlyMods|Bonded]: 60% increased [Freeze|Freeze] Buildup"],
                   "explicitMods":[{"description":"Adds 41 to 51 [Cold|Cold] Damage","flags":{"fractured":true}},
                                   {"description":"109% increased [ElementalDamage|Elemental] Damage with [Attack|Attacks]"},
                                   {"description":"101% increased [Physical] Damage","flags":{"desecrated":true}}],
                   "pseudoMods":["Sum: 42.5"]},
           "listing":{"price":{"amount":3,"currency":"divine","type":"buyout"},
                      "whisper":"@Seller3 Hi","account":{"name":"Seller3"}}}
        ]}
        """;

    private const string StaffSearchJson =
        """{"id":"testquery2","complexity":10,"result":["hashStaff"],"total":42}""";

    private FakeHttpHandler Setup(string? fetchJson = null, string? searchJson = null)
    {
        var fake = new FakeHttpHandler();
        fake.Responder = req =>
        {
            var url = req.RequestUri!.ToString();
            var json = url.Contains("/api/trade2/search/") ? (searchJson ?? SearchJson)
                     : url.Contains("/api/trade2/fetch/") ? (fetchJson ?? FetchJson)
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
        // лот без whisper парсится с пустой строкой, а не падает
        var second = result.Listings[1];
        Assert.Equal("", second.Whisper);
        Assert.Equal("Seller2", second.Seller);
    }

    // #50: смешанные формы модов (строки в runeMods + объекты в explicitMods)
    // роняли разбор блока результатов, и поиск возвращал сырую Lua-ошибку
    [Fact(Timeout = 30_000)]
    public async Task SearchTrade_MixedModShapes_ParsesListing()
    {
        Setup(StaffFetchJson, StaffSearchJson);
        var result = await _host.SearchTradeAsync("Standard", WeightQuery, CancellationToken.None);

        Assert.Null(result.Error);
        var listing = Assert.Single(result.Listings);
        Assert.Equal("Seller3", listing.Seller);
        Assert.Equal(42.5, listing.Weight);
        // рунный мод пришёл строкой — он не должен потеряться
        Assert.Contains("Adds 18 to 30 Cold Damage", listing.ItemText);
        // объектный мод разобран, скобочная разметка снята
        Assert.Contains("109% increased Elemental Damage with Attacks", listing.ItemText);
        // флаги мода превращаются в теги строки, понятные Item:ParseRaw
        Assert.Contains("{fractured}Adds 41 to 51 Cold Damage", listing.ItemText);
        Assert.Contains("{desecrated}101% increased Physical Damage", listing.ItemText);
        // "Implicits: N" совпадает с числом реально добавленных строк
        Assert.Contains("Implicits: 2", listing.ItemText);
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
