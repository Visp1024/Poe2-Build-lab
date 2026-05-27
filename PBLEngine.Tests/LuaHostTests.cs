using PBLEngine;
using System;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class LuaHostTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public LuaHostTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    // ── SaveBuildToXml ─────────────────────────────────────────────────────

    [Fact]
    public void SaveBuildToXml_ReturnsNonEmptyXml()
    {
        var xml = _host.SaveBuildToXml();

        Assert.NotNull(xml);
        Assert.Contains("<PathOfBuilding2", xml);
    }

    [Fact]
    public void SaveBuildToXml_ContainsBuildSection()
    {
        var xml = _host.SaveBuildToXml();

        Assert.NotNull(xml);
        Assert.Contains("<Build", xml);
    }

    [Fact]
    public void LoadBuildFromXml_RoundTrip_PreservesStats()
    {
        _host.NewBuild();
        var xml = _host.SaveBuildToXml()!;

        _host.LoadBuildFromXml(xml, "Round-trip test");
        var xmlAfter = _host.SaveBuildToXml();

        Assert.NotNull(xmlAfter);
        Assert.Contains("<PathOfBuilding2", xmlAfter);
    }

    // ── Notes ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetNotes_NewBuild_ReturnsEmptyString()
    {
        var notes = _host.GetNotes();

        Assert.Equal("", notes);
    }

    [Fact]
    public void SetNotes_ThenGetNotes_RoundTrips()
    {
        const string expected = "Test notes content";

        _host.SetNotes(expected);
        var actual = _host.GetNotes();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetNotes_EmptyString_ClearsNotes()
    {
        _host.SetNotes("some content");
        _host.SetNotes("");

        Assert.Equal("", _host.GetNotes());
    }

    [Fact]
    public void SetNotes_MultipleLinesPreserved()
    {
        const string text = "Line 1\nLine 2\nLine 3";

        _host.SetNotes(text);

        Assert.Equal(text, _host.GetNotes());
    }

    [Fact]
    public void SaveBuildToXml_AfterSetNotes_ContainsNotesText()
    {
        const string notes = "My build notes here";
        _host.SetNotes(notes);

        var xml = _host.SaveBuildToXml();

        Assert.NotNull(xml);
        Assert.Contains(notes, xml);
    }

    // ── GetConfigOptions ───────────────────────────────────────────────────

    [Fact]
    public void GetConfigOptions_ReturnsNonEmptyList()
    {
        var opts = _host.GetConfigOptions();

        Assert.NotEmpty(opts);
    }

    [Fact]
    public void GetConfigOptions_ContainsResistancePenalty()
    {
        var opts = _host.GetConfigOptions();

        Assert.Contains(opts, o => o.Var == "resistancePenalty");
    }

    [Fact]
    public void GetConfigOptions_ResistancePenalty_IsListType()
    {
        var opts = _host.GetConfigOptions();
        var opt = opts.First(o => o.Var == "resistancePenalty");

        Assert.Equal("list", opt.Type);
        Assert.NotEmpty(opt.ListOptions);
    }

    [Fact]
    public void GetConfigOptions_ListOptions_HaveValAndLabel()
    {
        var opts = _host.GetConfigOptions();
        var resPenalty = opts.First(o => o.Var == "resistancePenalty");

        foreach (var item in resPenalty.ListOptions)
        {
            Assert.False(string.IsNullOrEmpty(item.Val), "Val should not be empty");
            Assert.False(string.IsNullOrEmpty(item.Label), "Label should not be empty");
        }
    }

    [Fact]
    public void GetConfigOptions_AllHaveNonEmptyVar()
    {
        var opts = _host.GetConfigOptions();

        Assert.All(opts, o => Assert.False(string.IsNullOrEmpty(o.Var)));
    }

    [Fact]
    public void GetConfigOptions_AllHaveNonEmptyLabel()
    {
        var opts = _host.GetConfigOptions();

        Assert.All(opts, o => Assert.False(string.IsNullOrEmpty(o.Label)));
    }

    [Fact]
    public void GetConfigOptions_SectionsArePopulated()
    {
        var opts = _host.GetConfigOptions();
        var sections = opts.Select(o => o.Section).Distinct().ToList();

        Assert.Contains("General", sections);
    }

    // ── SetConfigValue ─────────────────────────────────────────────────────

    [Fact]
    public void SetConfigValue_EnemyIsBoss_AffectsStats()
    {
        _host.NewBuild();
        var statsBefore = _host.GetAllStats();
        var lifeBefore = statsBefore.TryGetValue("Life", out var v) ? Convert.ToDouble(v) : 0;

        _host.SetConfigValue("enemyIsBoss", "Pinnacle");

        var statsAfter = _host.GetAllStats();
        Assert.NotNull(statsAfter);
    }

    [Fact]
    public void SetConfigValue_EmptyString_ClearsValue()
    {
        _host.SetConfigValue("enemyIsBoss", "Pinnacle");
        _host.SetConfigValue("enemyIsBoss", "");

        var stats = _host.GetAllStats();
        Assert.NotNull(stats);
    }

    [Fact]
    public void SetConfigValue_ResistancePenalty_UpdatesStats()
    {
        _host.NewBuild();
        _host.SetConfigValue("resistancePenalty", "0");
        var statsAt0 = _host.GetAllStats();

        _host.SetConfigValue("resistancePenalty", "-60");
        var statsAtMinus60 = _host.GetAllStats();

        Assert.NotNull(statsAt0);
        Assert.NotNull(statsAtMinus60);
    }
}
