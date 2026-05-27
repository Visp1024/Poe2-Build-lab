using PBLEngine;
using System;
using System.IO;

namespace PBLEngine.Tests;

/// <summary>
/// Shared xUnit fixture: initialises LuaHost once per test class,
/// then resets to a fresh NewBuild() before each test via helpers.
/// LuaHost init takes ~40 s; this fixture amortises that cost.
/// </summary>
public sealed class LuaHostFixture : IDisposable
{
    public LuaHost Host { get; }

    public LuaHostFixture()
    {
        Host = new LuaHost();
        Host.Initialize(FindRepoRoot());
        Host.NewBuild();
    }

    public void Dispose() => Host.Dispose();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repo root (no src/ ancestor)");
    }
}
