using PBLEngine;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class LuaWorkerPoolTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public LuaWorkerPoolTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        var buildPath = Path.Combine(FindRepoRoot(), "tools", "parity", "community_builds",
            "CqX3fXBg_S0q7p54SFGHC.xml");
        _host.LoadBuildFromXml(File.ReadAllText(buildPath), "PoolTest");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("no src/ ancestor");
    }

    [Fact]
    public async Task Pool_ParityWithReferenceCoroutine()
    {
        // Эталон: PoB-корутина на главном хосте, глубина 3 (десятки нод — быстро).
        var reference = _host.BuildNodePower("FullDPS", maxDepth: 3);
        var refById = reference.Entries.Where(e => !e.Alloc && e.Power != 0)
                                       .ToDictionary(e => e.Id, e => e.Power);
        Assert.NotEmpty(refById);

        var xml = _host.SaveBuildToXml();
        Assert.False(string.IsNullOrEmpty(xml));
        var nodes = _host.GetPowerNodeList().Where(n => refById.ContainsKey(n.Id)).ToList();

        using var pool = new LuaWorkerPool(FindRepoRoot(), size: 1);
        var result = await NodePowerOrchestrator.RunAsync(
            xml!, "FullDPS", false, nodes, pool.EnsureStarted(), null, CancellationToken.None);

        Assert.Equal(nodes.Count, result.Entries.Count);
        foreach (var e in result.Entries)
        {
            var expected = refById[e.Id];
            Assert.True(Math.Abs(e.Power - expected) <= Math.Abs(expected) * 1e-6 + 1e-9,
                $"node {e.Id} ({e.Name}): pool={e.Power} reference={expected}");
        }
        Assert.True(pool.Ready >= 1);
    }
}
