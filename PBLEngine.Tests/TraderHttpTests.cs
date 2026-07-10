using PBLEngine;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>Подменный handler: отдаёт заданные ответы и записывает запросы.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    // Тело и Content-Type захватываем в момент запроса: HttpRequestMessage
    // диспозится отправителем сразу после SendAsync, читать позже нельзя.
    public List<string?> Bodies { get; } = [];
    public List<string?> ContentTypes { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
        Requests.Add(request);
        return Responder(request);
    }
}

[Collection("LuaHost")]
public class TraderHttpTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public TraderHttpTests(LuaHostFixture fixture) => _host = fixture.Host;

    [Fact]
    public async Task DownloadPage_DeliversBodyHeaderAndStatusLine_ToLuaCallback()
    {
        var fake = new FakeHttpHandler
        {
            Responder = _ =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("hello-body") };
                resp.Headers.Add("X-Rate-Limit-Ip", "8:10:60");
                return resp;
            },
        };
        _host.TraderHttpHandler = fake;
        _host.EnsureTraderHttp();

        _host.State.DoString(@"
            _testResult = nil
            launch:DownloadPage('https://example.test/x', function(response, errMsg)
                _testResult = { body = response.body, header = response.header, err = errMsg }
            end)");

        await WaitForDrainAsync();

        var body = (string)_host.State.DoString("return _testResult.body")[0];
        var header = (string)_host.State.DoString("return _testResult.header")[0];
        Assert.Equal("hello-body", body);
        Assert.Contains("HTTP/1.1 200", header);                 // статус-строку матчит PoEAPI.lua:189
        Assert.Contains("X-Rate-Limit-Ip: 8:10:60", header);     // rate-limit заголовки доходят до Lua
    }

    [Fact]
    public async Task DownloadPage_WithBody_SendsPost_WithHeaders()
    {
        var fake = new FakeHttpHandler();
        _host.TraderHttpHandler = fake;
        _host.EnsureTraderHttp();

        _host.State.DoString(@"
            _testDone = false
            launch:DownloadPage('https://example.test/api', function() _testDone = true end,
                { header = 'Content-Type: application/json\nAuthorization: Bearer tok123',
                  body = '{""q"":1}' })");
        await WaitForDrainAsync();

        var req = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("Bearer tok123", req.Headers.Authorization!.ToString());
        Assert.Equal("application/json", Assert.Single(fake.ContentTypes));
        Assert.Equal("{\"q\":1}", Assert.Single(fake.Bodies));
        Assert.True((bool)_host.State.DoString("return _testDone")[0]);
    }

    [Fact]
    public async Task DownloadPage_NetworkError_PassesErrMsg()
    {
        var fake = new FakeHttpHandler { Responder = _ => throw new HttpRequestException("boom") };
        _host.TraderHttpHandler = fake;
        _host.EnsureTraderHttp();

        _host.State.DoString(@"
            _testErr = nil
            launch:DownloadPage('https://example.test/x', function(r, e) _testErr = e end)");
        await WaitForDrainAsync();

        var err = (string)_host.State.DoString("return _testErr")[0];
        Assert.Contains("boom", err);
    }

    private async Task WaitForDrainAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            if (_host.DrainTraderHttp() > 0) return;
            await Task.Delay(50);
        }
        Assert.Fail("HTTP bridge did not deliver a completion in 5s");
    }
}
