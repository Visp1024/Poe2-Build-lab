using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PBLEngine;

/// <summary>
/// Encodes/decodes PoB share codes: base64url( zlib( UTF-8 XML ) ).
/// Compatible with the Lua Deflate/Inflate + common.base64 used by ImportTab.
/// </summary>
public static class BuildCodec
{
    public static string Encode(string xml)
    {
        var data = Encoding.UTF8.GetBytes(xml);
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return Convert.ToBase64String(ms.ToArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string? Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            var b64 = code.Trim().Replace('-', '+').Replace('_', '/');
            var pad = (4 - b64.Length % 4) % 4;
            if (pad < 4) b64 += new string('=', pad);

            var compressed = Convert.FromBase64String(b64);
            using var ms = new MemoryStream(compressed);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    public static bool LooksLikeCode(string input)
        => !string.IsNullOrWhiteSpace(input)
           && !input.StartsWith("http", StringComparison.OrdinalIgnoreCase)
           && input.Length > 10;
}
