using PBLEngine;
using System;
using System.IO;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Regression: when PBLApp is installed under a path containing non-ASCII
/// characters (e.g. Cyrillic «D:\Игры\PoB\…»), loading a build pulls in Lua data
/// files (tree.lua, etc.) via Lua's io.open/loadfile. On Windows those go through
/// the narrow CRT fopen, which interprets the UTF-8 path bytes under the C locale
/// and fails to find the file. LuaHost.Initialize must set the CRT ctype locale to
/// UTF-8 so fopen resolves Unicode paths.
/// </summary>
[Collection("LuaHost")]
public class UnicodePathTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public UnicodePathTests(LuaHostFixture fixture) => _host = fixture.Host;

    [Fact]
    public void Lua_CanOpenFile_WithCyrillicPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PBL_Тест_Кириллица");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "файл.lua");
        File.WriteAllText(file, "return 123");

        try
        {
            var luaPath = file.Replace('\\', '/');
            var result = _host.State.DoString($"return (loadfile([[{luaPath}]]) ~= nil)");

            Assert.NotNull(result);
            Assert.True(result.Length > 0 && result[0] is bool b && b,
                "Lua loadfile() must open a file whose path contains Cyrillic characters");
        }
        finally
        {
            try { File.Delete(file); Directory.Delete(dir); } catch { /* best effort */ }
        }
    }
}
