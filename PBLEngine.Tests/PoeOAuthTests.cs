using PBLApp.Core.Trader;
using System;
using System.Text;
using Xunit;

namespace PBLEngine.Tests;

public class PoeOAuthTests
{
    [Fact]
    public void CreatePkcePair_ChallengeIsBase64UrlSha256OfVerifier()
    {
        var (verifier, challenge) = PoeOAuthService.CreatePkcePair();

        using var sha = System.Security.Cryptography.SHA256.Create();
        var expected = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.Equal(expected, challenge);
        // base64url без паддинга
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.DoesNotContain('=', verifier);
        Assert.True(verifier.Length >= 43, $"verifier too short: {verifier.Length}"); // RFC 7636 минимум
    }

    [Fact]
    public void CreatePkcePair_VerifiersAreUnique()
    {
        var (v1, _) = PoeOAuthService.CreatePkcePair();
        var (v2, _) = PoeOAuthService.CreatePkcePair();
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void BuildAuthorizeUrl_HasPobClientIdScopesAndRedirectUri()
    {
        var url = PoeOAuthService.BuildAuthorizeUrl("aabbccdd11223344", "CHLG", 49082);

        Assert.StartsWith("https://www.pathofexile.com/oauth/authorize?", url);
        Assert.Contains("client_id=pob", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("state=aabbccdd11223344", url);
        Assert.Contains("code_challenge=CHLG", url);
        Assert.Contains("code_challenge_method=S256", url);
        // scope как в PoEAPI.lua:5-10, пробелы как %20
        Assert.Contains("account:profile%20account:leagues%20account:characters%20account:trade", url);
        // redirect_uri обязателен и добавляется как в LaunchServer.lua:32 (без URL-кодирования)
        Assert.Contains("&redirect_uri=http://localhost:49082", url);
    }

    [Fact]
    public void RedirectPorts_AreTheRegisteredPobPorts()
    {
        // GGG-клиент "pob" принимает redirect только на эти порты (LaunchServer.lua:11)
        Assert.Equal([49082, 49083, 49084], PoeOAuthService.RedirectPorts);
    }
}
