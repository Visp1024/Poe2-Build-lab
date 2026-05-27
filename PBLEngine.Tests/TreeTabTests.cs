using PBLEngine;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TreeTabTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TreeTabTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    // ── GetTreeData: nodes ─────────────────────────────────────────────────

    [Fact]
    public void GetTreeData_ReturnsNonEmptyNodeList()
    {
        var (nodes, _) = _host.GetTreeData();

        Assert.NotEmpty(nodes);
    }

    [Fact]
    public void GetTreeData_AllNodesHavePositions()
    {
        var (nodes, _) = _host.GetTreeData();

        foreach (var n in nodes)
        {
            Assert.True(n.X != 0 || n.Y != 0, $"Node {n.Id} ({n.Name}) has zero position");
        }
    }

    [Fact]
    public void GetTreeData_AllNodesHaveNonEmptyType()
    {
        var (nodes, _) = _host.GetTreeData();

        foreach (var n in nodes)
            Assert.False(string.IsNullOrEmpty(n.Type), $"Node {n.Id} has empty type");
    }

    [Fact]
    public void GetTreeData_HasNormalAndNotableNodes()
    {
        var (nodes, _) = _host.GetTreeData();

        Assert.Contains(nodes, n => n.Type == "Normal");
        Assert.Contains(nodes, n => n.Type == "Notable");
    }

    [Fact]
    public void GetTreeData_HasKeystoneNodes()
    {
        var (nodes, _) = _host.GetTreeData();

        Assert.Contains(nodes, n => n.Type == "Keystone");
    }

    [Fact]
    public void GetTreeData_HasClassStartNodes()
    {
        var (nodes, _) = _host.GetTreeData();

        Assert.Contains(nodes, n => n.Type == "ClassStart");
    }

    [Fact]
    public void GetTreeData_NoOnlyImageNodes()
    {
        var (nodes, _) = _host.GetTreeData();

        Assert.DoesNotContain(nodes, n => n.Type == "OnlyImage");
    }

    [Fact]
    public void GetTreeData_NodeIdsAreUnique()
    {
        var (nodes, _) = _host.GetTreeData();

        var ids = nodes.Select(n => n.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void GetTreeData_NotableNodesHaveNames()
    {
        var (nodes, _) = _host.GetTreeData();

        var notables = nodes.Where(n => n.Type == "Notable").ToList();
        Assert.NotEmpty(notables);
        Assert.All(notables, n => Assert.False(string.IsNullOrEmpty(n.Name),
            $"Notable node {n.Id} has empty name"));
    }

    [Fact]
    public void GetTreeData_KeystoneNodesHaveStats()
    {
        var (nodes, _) = _host.GetTreeData();

        var keystones = nodes.Where(n => n.Type == "Keystone").ToList();
        Assert.NotEmpty(keystones);
        Assert.All(keystones, n => Assert.NotEmpty(n.Stats));
    }

    // ── GetTreeData: allocated ─────────────────────────────────────────────

    [Fact]
    public void GetTreeData_NewBuild_HasAllocatedClassStartNode()
    {
        var (nodes, allocated) = _host.GetTreeData();

        // New build always has at least a class start node allocated
        Assert.NotEmpty(allocated);
        var nodeById = nodes.ToDictionary(n => n.Id);
        Assert.True(allocated.All(id => nodeById.ContainsKey(id)),
            "All allocated IDs should exist in the node list");
    }

    [Fact]
    public void GetTreeData_AllocatedNodesBelongToNodeList()
    {
        var (nodes, allocated) = _host.GetTreeData();

        var validIds = nodes.Select(n => n.Id).ToHashSet();
        foreach (var id in allocated)
            Assert.Contains(id, validIds);
    }

    // ── GetTreeData: connections ───────────────────────────────────────────

    [Fact]
    public void GetTreeData_MostNodesHaveConnections()
    {
        var (nodes, _) = _host.GetTreeData();

        // At least 70% of non-ClassStart nodes should have connections
        var regular = nodes.Where(n => n.Type != "ClassStart" && n.Type != "AscendClassStart").ToList();
        int withLinks = regular.Count(n => n.LinkedIds.Length > 0);
        Assert.True(withLinks >= regular.Count * 0.7,
            $"Only {withLinks}/{regular.Count} non-start nodes have connections");
    }

    [Fact]
    public void GetTreeData_LinkedIdsReferenceExistingNodes()
    {
        var (nodes, _) = _host.GetTreeData();

        var validIds = nodes.Select(n => n.Id).ToHashSet();
        foreach (var node in nodes)
            foreach (var lid in node.LinkedIds)
                Assert.Contains(lid, validIds);
    }
}
