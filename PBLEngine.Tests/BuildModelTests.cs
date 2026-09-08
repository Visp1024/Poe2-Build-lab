using System;
using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class BuildModelTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public BuildModelTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    // ── Notes property ─────────────────────────────────────────────────────

    [Fact]
    public void Notes_NewBuild_IsEmpty()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        Assert.Equal("", model.Notes);
    }

    [Fact]
    public void Notes_Set_UpdatesLuaState()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        model.Notes = "Test notes";

        Assert.Equal("Test notes", _host.GetNotes());
    }

    [Fact]
    public void Notes_Set_RaisesPropertyChanged()
    {
        var model = new BuildModel(_host);
        model.NewBuild();
        string? changedProp = null;
        model.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        model.Notes = "Changed";

        Assert.Equal(nameof(BuildModel.Notes), changedProp);
    }

    [Fact]
    public void Notes_SetSameValue_DoesNotRaisePropertyChanged()
    {
        var model = new BuildModel(_host);
        model.NewBuild();
        model.Notes = "Same";
        int changeCount = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildModel.Notes))
                changeCount++;
        };

        model.Notes = "Same";

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void Refresh_AfterSetNotes_ReadsNotesFromLua()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        _host.SetNotes("Externally set");
        model.Refresh();

        Assert.Equal("Externally set", model.Notes);
    }

    // ── Core stats ─────────────────────────────────────────────────────────

    [Fact]
    public void NewBuild_Life_IsPositive()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        Assert.True(model.Life > 0, $"Life should be positive, got {model.Life}");
    }

    [Fact]
    public void NewBuild_TotalEhp_IsPositiveAndMatchesRawStat()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        Assert.True(model.TotalEhp > 0, $"TotalEhp should be positive, got {model.TotalEhp}");
        Assert.True(model.HasEhp);
        Assert.Equal(Convert.ToDouble(model.AllStats["TotalEHP"]), model.TotalEhp, 6);
    }

    [Fact]
    public void NewBuild_EhpTooltipStats_AreRead()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        Assert.True(model.HitsBeforeDeath > 0, $"HitsBeforeDeath should be positive, got {model.HitsBeforeDeath}");
        Assert.True(model.SurvivalTime > 0, $"SurvivalTime should be positive, got {model.SurvivalTime}");
    }

    [Fact]
    public void NewBuild_AllStats_IsNotEmpty()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        Assert.NotEmpty(model.AllStats);
    }

    [Fact]
    public void Refresh_RaisesPropertyChangedForAllStats()
    {
        var model = new BuildModel(_host);
        model.NewBuild();
        bool allStatsChanged = false;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildModel.AllStats))
                allStatsChanged = true;
        };

        model.Refresh();

        Assert.True(allStatsChanged);
    }

    // ── SaveBuildToXml ─────────────────────────────────────────────────────

    [Fact]
    public void SaveBuildToXml_AfterNewBuild_ReturnsXml()
    {
        var model = new BuildModel(_host);
        model.NewBuild();

        var xml = model.SaveBuildToXml();

        Assert.NotNull(xml);
        Assert.Contains("<PathOfBuilding2", xml);
    }

    [Fact]
    public void SaveBuildToXml_IncludesNotesContent()
    {
        var model = new BuildModel(_host);
        model.NewBuild();
        model.Notes = "Saved notes content";

        var xml = model.SaveBuildToXml();

        Assert.NotNull(xml);
        Assert.Contains("Saved notes content", xml);
    }

    // ── LoadBuildFromXml ───────────────────────────────────────────────────

    [Fact]
    public void LoadBuildFromXml_SetsStatsProperly()
    {
        var model = new BuildModel(_host);
        model.NewBuild();
        var xml = model.SaveBuildToXml()!;

        model.LoadBuildFromXml(xml, "Loaded");

        Assert.True(model.Life > 0);
    }

    [Fact]
    public void LoadBuildFromXml_PreservesNotes()
    {
        var model = new BuildModel(_host);
        model.NewBuild();
        model.Notes = "Round-trip notes";
        var xml = model.SaveBuildToXml()!;

        model.LoadBuildFromXml(xml, "Loaded");

        Assert.Equal("Round-trip notes", model.Notes);
    }
}
