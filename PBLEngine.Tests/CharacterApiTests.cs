using PBLApp.Core.Import;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

public class CharacterApiTests
{
    // Форма ответа api.pathofexile.com/character/poe2
    private const string ListJson =
        """
        {"characters":[
          {"name":"Zulhammaz","class":"Titan","level":92,"league":"Rise of the Abyssal"},
          {"name":"Alpha","class":"Invoker","level":31,"league":"Standard"}
        ]}
        """;

    private static CharacterApi Api(FakeHttpHandler handler, string? token = "tok")
        => new(_ => Task.FromResult(token), handler);

    private static FakeHttpHandler Responding(HttpStatusCode code, string body = "{}")
        => new()
        {
            Responder = _ => new HttpResponseMessage(code)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") },
        };

    [Fact]
    public async Task GetCharacters_ParsesListAndSendsBearerToken()
    {
        var fake = Responding(HttpStatusCode.OK, ListJson);
        var result = await Api(fake).GetCharactersAsync();

        Assert.True(result.Ok);
        Assert.Equal(2, result.Value!.Count);
        var first = result.Value[0];
        Assert.Equal("Zulhammaz", first.Name);
        Assert.Equal("Titan", first.Class);
        Assert.Equal(92, first.Level);
        Assert.Equal("Rise of the Abyssal", first.League);

        var req = Assert.Single(fake.Requests);
        Assert.Equal("https://api.pathofexile.com/character/poe2", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetCharacters_EmptyAccountIsNotAnError()
    {
        var result = await Api(Responding(HttpStatusCode.OK, """{"characters":[]}""")).GetCharactersAsync();

        Assert.True(result.Ok);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetCharacters_WithoutToken_DoesNotCallApi()
    {
        var fake = Responding(HttpStatusCode.OK, ListJson);
        var result = await Api(fake, token: null).GetCharactersAsync();

        Assert.Equal(CharacterApiError.NotAuthenticated, result.Error);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CharacterApiError.NotAuthenticated)]
    [InlineData(HttpStatusCode.Forbidden,    CharacterApiError.PrivateProfile)]
    [InlineData(HttpStatusCode.NotFound,     CharacterApiError.NotFound)]
    [InlineData(HttpStatusCode.BadGateway,   CharacterApiError.Network)]
    public async Task GetCharacters_MapsStatusCodesToErrors(HttpStatusCode code, CharacterApiError expected)
    {
        var result = await Api(Responding(code)).GetCharactersAsync();

        Assert.False(result.Ok);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task GetCharacters_RateLimited_ReportsRetryAfter()
    {
        var fake = new FakeHttpHandler
        {
            Responder = _ =>
            {
                var resp = new HttpResponseMessage((HttpStatusCode)429)
                    { Content = new StringContent("") };
                resp.Headers.Add("Retry-After", "34");
                return resp;
            },
        };

        var result = await Api(fake).GetCharactersAsync();

        Assert.Equal(CharacterApiError.RateLimited, result.Error);
        Assert.Equal(34, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetCharacters_RateLimitedWithoutHeader_FallsBackToAMinute()
    {
        var result = await Api(Responding((HttpStatusCode)429, "")).GetCharactersAsync();

        Assert.Equal(CharacterApiError.RateLimited, result.Error);
        Assert.Equal(60, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetCharacterJson_EscapesNameAndReturnsRawBody()
    {
        const string body = """{"character":{"name":"Дед Инсайд","class":"Titan"}}""";
        var fake = Responding(HttpStatusCode.OK, body);

        var result = await Api(fake).GetCharacterJsonAsync("Дед Инсайд");

        Assert.True(result.Ok);
        Assert.Equal(body, result.Value);
        // В сеть уходит AbsoluteUri — там имя должно быть процентно-закодировано
        // (пробел и кириллица), иначе GGG вернёт 404.
        var url = Assert.Single(fake.Requests).RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://api.pathofexile.com/character/poe2/", url);
        Assert.Contains("%20", url);
        Assert.DoesNotContain(' ', url);
    }
}
