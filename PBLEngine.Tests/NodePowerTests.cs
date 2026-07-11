using PBLEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class NodePowerTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public NodePowerTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        // Load a real community build (Sorceress/Stormweaver, CombinedDPS ~42k) so
        // power calculations have something to measure.  Community builds store
        // includeInFullDPS=false on every socket group (FullDPS uses a separate
        // per-group toggle), so we enable it on the main group after loading so
        // the FullDPS stat is non-zero.
        var buildPath = Path.Combine(RepoRoot(), "tools", "parity", "community_builds",
            "CqX3fXBg_S0q7p54SFGHC.xml");
        _host.LoadBuildFromXml(File.ReadAllText(buildPath), "NodePowerTest");
        // Enable FullDPS roll-up for the main socket group.
        // build.mainSocketGroup is an index into skillsTab.socketGroupList.
        _host.State.DoString(@"
            local idx = build.mainSocketGroup or 1
            local sg = build.skillsTab.socketGroupList and build.skillsTab.socketGroupList[idx]
            if sg then sg.includeInFullDPS = true end
        ");
    }

    private static string RepoRoot()
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

    [Fact]
    public void GetPowerStatList_ReturnsOptions_IncludingOffDefDefault()
    {
        var list = _host.GetPowerStatList();

        Assert.NotEmpty(list);
        Assert.Contains(list, o => o.CombinedOffDef);            // Offence/Defence default
        Assert.Contains(list, o => o.StatKey == "FullDPS");      // a known single stat
    }

    [Fact]
    public void GetPowerStatList_ExcludesItemOnlyEntries()
    {
        var list = _host.GetPowerStatList();

        // The "Name" entry has ignoreForNodes/itemField set and must not appear in the
        // node heat-map list. This is a real filter check — if the filter broke, "Name"
        // would be included and this assertion would catch it.
        Assert.DoesNotContain(list, o => o.Label == "Name");
    }

    [Fact]
    public void BuildNodePower_SingleStat_TopNodeHasPositivePower()
    {
        var res = _host.BuildNodePower("FullDPS");

        Assert.False(res.OffDefMode);
        Assert.NotEmpty(res.Entries);
        // Strongest node for a DPS stat must move the stat upward.
        var top = res.Entries.OrderByDescending(e => e.Power).First();
        Assert.True(top.Power > 0, $"top node power was {top.Power}");
        Assert.True(res.Max.SingleStat > 0);
    }

    [Fact]
    public void BuildNodePower_OffDefMode_ReturnsBothChannels()
    {
        var res = _host.BuildNodePower(null);   // null = Offence/Defence default

        Assert.True(res.OffDefMode);
        Assert.NotEmpty(res.Entries);
        Assert.True(res.Max.Offence  > 0, "no offence maximum");
        Assert.True(res.Max.Defence  > 0, "no defence maximum");
    }

    [Fact]
    public void BuildNodePower_ReportsProgressToCompletion()
    {
        int last = -1;
        _host.BuildNodePower("Life", null, pc => last = pc);

        Assert.True(last >= 0);   // callback fired at least once
    }

    [Fact]
    public void BuildNodePower_OnlyReturnsAllocatableTreeNodeTypes()
    {
        var res = _host.BuildNodePower("Life");

        Assert.All(res.Entries, e =>
            Assert.Contains(e.Type, new[] { "Normal", "Notable", "Keystone" }));
    }

    [Fact]
    public void BuildNodePower_CancelsPromptly_ReturnsEmpty()
    {
        // A full build over the ~4500-node tree takes tens of seconds; cancelling
        // shortly after it starts must abort within ~one coroutine slice and return
        // an empty result rather than running to completion.
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => _host.BuildNodePower("FullDPS", null, null, cts.Token));
        Thread.Sleep(250);   // let a few coroutine slices run
        cts.Cancel();
        var res = task.GetAwaiter().GetResult();
        sw.Stop();

        Assert.Empty(res.Entries);                                 // cancelled → no rows
        Assert.True(sw.Elapsed.TotalSeconds < 20,                  // aborted, not run to completion
            $"cancel took {sw.Elapsed.TotalSeconds:0.0}s — did the token check run?");

        // State must be clean for the next build: a fresh build still succeeds.
        var res2 = _host.BuildNodePower("FullDPS");
        Assert.NotEmpty(res2.Entries);
    }

    [Fact]
    public void GetPowerNodeList_StepsMatchHoverPathLen()
    {
        var list = _host.GetPowerNodeList();
        Assert.NotEmpty(list);

        // Взятые и кластерные — без шагов.
        Assert.All(list.Where(n => n.Alloc || n.IsCluster), n => Assert.Null(n.Steps));

        // Для выборки достижимых невзятых нод Steps == PathLength из ховера
        // (ховер — независимый, визуально проверенный источник длины пути).
        var sample = list.Where(n => !n.Alloc && !n.IsCluster && n.Steps is > 0)
                         .OrderBy(n => n.Id).Take(20).ToList();
        Assert.NotEmpty(sample);
        foreach (var n in sample)
        {
            var hover = _host.GetNodeHoverInfo(n.Id);
            Assert.NotNull(hover);
            Assert.Equal(hover!.PathLength, n.Steps);
        }
    }

    [Fact]
    public void GetPowerNodeList_FiltersAscendancyAndEmptyModKey()
    {
        var list = _host.GetPowerNodeList();
        Assert.All(list, n => Assert.NotEqual("", n.ModKey));
        Assert.All(list, n => Assert.Contains(n.Type, new[] { "Normal", "Notable", "Keystone" }));
    }

    [Fact]
    public void PowerSession_MatchesBuildNodePower_OnSmallDepth()
    {
        // Эталон: оригинальная PoB-корутина, ограниченная глубиной 3 (быстро).
        var reference = _host.BuildNodePower("FullDPS", maxDepth: 3);
        var refById = reference.Entries.Where(e => !e.Alloc && e.Power != 0)
                                       .ToDictionary(e => e.Id, e => e.Power);
        Assert.NotEmpty(refById);

        // Кандидаты: те же ноды через session-API.
        var ids = refById.Keys.OrderBy(i => i).Take(40).ToList();
        _host.BeginPowerSession("FullDPS");
        try
        {
            var rows = _host.ComputePowerBatch(ids);
            Assert.Equal(ids.Count, rows.Count);
            foreach (var r in rows)
            {
                var expected = refById[r.Id];
                Assert.True(Math.Abs(r.Power - expected) <= Math.Abs(expected) * 1e-6 + 1e-9,
                    $"node {r.Id}: session={r.Power} reference={expected}");
            }
        }
        finally { _host.EndPowerSession(); }
    }

    [Fact]
    public void PowerSession_DoesNotPerturbMainOutput()
    {
        // Спека: расчёт не должен менять статы билда. Снимок до/после сессии.
        var before = _host.GetStat("TotalDPS");
        _host.BeginPowerSession("FullDPS");
        try
        {
            var ids = _host.GetPowerNodeList().Where(n => !n.Alloc && n.Steps is > 0)
                           .Take(10).Select(n => n.Id).ToList();
            _host.ComputePowerBatch(ids);
        }
        finally { _host.EndPowerSession(); }
        _host.RecalcStats();
        Assert.Equal(before, _host.GetStat("TotalDPS"));
    }

    [Fact]
    public void PowerSession_PathBatch_ReturnsPathPowerForMultiStepNodes()
    {
        var nodes = _host.GetPowerNodeList()
            .Where(n => !n.Alloc && !n.IsCluster && n.Steps is > 1).Take(5).ToList();
        Assert.NotEmpty(nodes);
        _host.BeginPowerSession("FullDPS");
        try
        {
            _host.ComputePowerBatch(nodes.Select(n => n.Id).ToList());
            var rows = _host.ComputePathPowerBatch(nodes.Select(n => n.Id).ToList());
            Assert.Equal(nodes.Count, rows.Count);
            Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.PerPointStr)));
        }
        finally { _host.EndPowerSession(); }
    }
}
