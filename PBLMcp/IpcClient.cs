using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLMcp;

/// <summary>
/// HTTP client that talks to PBLApp's IpcServer over localhost.
/// Reads port+token from %LOCALAPPDATA%\PathOfBuilding\mcp-ipc.json.
/// </summary>
public sealed class IpcClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static string DiscoveryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "mcp-ipc.json");

    public record Discovery(int Port, string Token, int Pid, string StartedAt);

    public static Discovery? TryReadDiscovery()
    {
        try
        {
            if (!File.Exists(DiscoveryPath)) return null;
            using var s = File.OpenRead(DiscoveryPath);
            using var doc = JsonDocument.Parse(s);
            var r = doc.RootElement;
            return new Discovery(
                r.GetProperty("port").GetInt32(),
                r.GetProperty("token").GetString() ?? "",
                r.TryGetProperty("pid", out var p) ? p.GetInt32() : 0,
                r.TryGetProperty("startedAt", out var t) ? t.GetString() ?? "" : "");
        }
        catch { return null; }
    }

    /// <summary>Check if PBLApp IPC is up: discovery file present + /ping responds.</summary>
    public static async Task<bool> IsAliveAsync(CancellationToken ct = default)
    {
        var d = TryReadDiscovery();
        if (d is null) return false;
        try
        {
            var resp = await CallRawAsync(d, "GET", "/ping", null, ct);
            return resp.Contains("\"ok\":true");
        }
        catch { return false; }
    }

    /// <summary>Wait until PBLApp IPC server starts responding (or timeout).</summary>
    public static async Task<bool> WaitForReadyAsync(int timeoutSeconds, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsAliveAsync(ct)) return true;
            try { await Task.Delay(300, ct); } catch { return false; }
        }
        return false;
    }

    public static async Task<string> CallAsync(string method, string endpoint, object? body = null, CancellationToken ct = default)
    {
        var d = TryReadDiscovery()
            ?? throw new InvalidOperationException(
                "PBLApp IPC discovery file not found. Is PBLApp running with --enable-ipc? " +
                $"Expected: {DiscoveryPath}");
        return await CallRawAsync(d, method, endpoint, body, ct);
    }

    private static async Task<string> CallRawAsync(
        Discovery d, string method, string endpoint, object? body, CancellationToken ct)
    {
        var url = $"http://127.0.0.1:{d.Port}{endpoint}";
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        req.Headers.Add("X-PoB-Token", d.Token);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var resp = await Http.SendAsync(req, ct);
        var content = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"IPC {method} {endpoint} → HTTP {(int)resp.StatusCode}: {content}");
        return content;
    }
}
