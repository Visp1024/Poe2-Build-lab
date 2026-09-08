using PBLApp.Core.Trader;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>Обновление access-токена по refresh_token (#47) — RefreshAsync ничего
/// не пишет в prefs, поэтому тестируется без состояния машины.</summary>
public class PoeOAuthRefreshTests
{
    private const string TokenJson =
        """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":3600}""";

    [Fact]
    public async Task RefreshAsync_PostsRefreshGrantAndParsesTokens()
    {
        var fake = new FakeHttpHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(TokenJson, Encoding.UTF8, "application/json") },
        };
        var svc = new PoeOAuthService(host: null, fake);

        var result = await svc.RefreshAsync("old-refresh");

        Assert.NotNull(result);
        Assert.Equal("new-access", result!.Value.Access);
        Assert.Equal("new-refresh", result.Value.Refresh);
        Assert.Equal(3600, result.Value.ExpiresIn);

        var req = Assert.Single(fake.Requests);
        Assert.Equal("https://www.pathofexile.com/oauth/token", req.RequestUri!.ToString());
        var body = Assert.Single(fake.Bodies);
        Assert.Contains("grant_type=refresh_token", body);
        Assert.Contains("refresh_token=old-refresh", body);
        Assert.Contains("client_id=pob", body);
    }

    [Fact]
    public async Task RefreshAsync_RejectedRefreshToken_ReturnsNull()
    {
        var fake = new FakeHttpHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("""{"error":"invalid_grant"}""") },
        };
        var svc = new PoeOAuthService(host: null, fake);

        Assert.Null(await svc.RefreshAsync("expired"));
    }
}
