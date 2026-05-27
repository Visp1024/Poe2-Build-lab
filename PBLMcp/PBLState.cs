using PBLEngine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PBLMcp;

/// <summary>
/// Singleton that holds the LuaHost for the lifetime of the MCP server process.
/// Initialization starts immediately in a background Task so the first tool call
/// doesn't block for 20 seconds.
/// </summary>
public sealed class PBLState : IDisposable
{
    // Starts immediately in the background when PBLState is constructed (DI singleton = at startup)
    private readonly Task<LuaHost> _initTask = Task.Run(CreateAndInitialize);

    /// <summary>Returns the initialised LuaHost, blocking until ready.</summary>
    public LuaHost Host => _initTask.GetAwaiter().GetResult();

    /// <summary>The raw init task — use in constructors that want to await instead of block.</summary>
    public Task<LuaHost> HostTask => _initTask;

    private static LuaHost CreateAndInitialize()
    {
        var host = new LuaHost();

        // Redirect Lua print/io.write to stderr BEFORE HeadlessWrapper prints startup lines,
        // so MCP JSON-RPC stdout stays clean.
        host.State.DoString(@"
            local _stderr = io.stderr
            print = function(...)
                local parts = {}
                for i = 1, select('#', ...) do parts[i] = tostring(select(i, ...)) end
                _stderr:write(table.concat(parts, '\t') .. '\n')
            end
            io.write = function(...) _stderr:write(...) end
        ");

        host.Initialize(FindRepoRoot());

        // Blank build so stats are always available on first tool call
        host.NewBuild();
        return host;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Cannot find repo root (no src/ above " + AppContext.BaseDirectory + ")");
    }

    public void Dispose() => _initTask.Result.Dispose();
}
