using PBLEngine;
using System;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Regression tests for Lua 5.4 compatibility crashes fixed in compat.lua and LuaHost.
/// </summary>
[Collection("LuaHost")]
public class RegressionTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public RegressionTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    // ── math.pow shim (compat.lua) ─────────────────────────────────────────
    // CalcOffence.lua line 19: local m_pow = math.pow
    // math.pow was removed in Lua 5.2; CalcOffence uses it for ailment crit chance.

    [Fact]
    public void Compat_MathPow_IsAvailable()
    {
        var result = _host.State.DoString("return math.pow(2, 10)");
        Assert.Equal(1024.0, Convert.ToDouble(result[0]), precision: 0);
    }

    [Fact]
    public void Compat_MathPow_ZeroExponent_ReturnsOne()
    {
        var result = _host.State.DoString("return math.pow(5, 0)");
        Assert.Equal(1.0, Convert.ToDouble(result[0]), precision: 0);
    }

    [Fact]
    public void Compat_MathPow_FractionalExponent_Works()
    {
        var result = _host.State.DoString("return math.pow(4, 0.5)");
        Assert.Equal(2.0, Convert.ToDouble(result[0]), precision: 5);
    }

    [Fact]
    public void Compat_MathPow_EqualsCaretOperator()
    {
        var result = _host.State.DoString("return math.pow(3, 7) == 3^7");
        Assert.Equal(true, result[0]);
    }

    // ── GetAllStats does not crash via m_pow (CalcOffence:4862) ───────────
    // Regression: if math.pow is nil, BuildOutput() crashes mid-calculation.

    [Fact]
    public void GetAllStats_DoesNotCrash_WhenAilmentCalcsRun()
    {
        var stats = _host.GetAllStats();

        Assert.NotNull(stats);
        Assert.NotEmpty(stats);
    }

    [Fact]
    public void GetAllStats_WithAilmentEnabled_DoesNotCrash()
    {
        _host.SetConfigValue("igniteEnable", "true");

        var stats = _host.GetAllStats();

        Assert.NotNull(stats);
        Assert.NotEmpty(stats);
    }

    // ── SaveBuildToXml when mainOutput is nil (Build.lua:1018) ────────────
    // Regression: SaveDB accessed mainOutput[statData.stat] without nil guard.
    // Fix: LuaHost.SaveBuildToXml() calls BuildOutput() if mainOutput is nil.

    [Fact]
    public void SaveBuildToXml_WhenMainOutputNil_DoesNotCrash()
    {
        _host.State.DoString("if build and build.calcsTab then build.calcsTab.mainOutput = nil end");

        var xml = _host.SaveBuildToXml();

        Assert.NotNull(xml);
        Assert.Contains("<PathOfBuilding", xml);
    }

    [Fact]
    public void SaveBuildToXml_AfterMainOutputNilCleared_RestoresOutput()
    {
        _host.State.DoString("if build and build.calcsTab then build.calcsTab.mainOutput = nil end");
        _host.SaveBuildToXml();

        var stats = _host.GetAllStats();

        Assert.NotNull(stats);
        Assert.NotEmpty(stats);
    }
}
