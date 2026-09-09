using PBLEngine;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Грейд модификаторов (задача #51): PBLModTiers считает тир по пулу модов базы,
/// T1 = лучший тир (как показывает игра). Внутренняя нумерация крафт-панели PoB
/// обратная, поэтому здесь важно зафиксировать именно направление.
/// </summary>
[Collection("LuaHost")]
public class ModTierTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public ModTierTests(LuaHostFixture fixture) => _host = fixture.Host;

    private ModTierInfo? Resolve(string baseName, string line) =>
        _host.ResolveModTiers(baseName, new[] { line }).FirstOrDefault();

    [Fact]
    public void HighestRoll_IsTierOne()
    {
        // +200-214 — верхняя серия IncreasedLife; для пояса это лучший доступный тир.
        var best = Resolve("Heavy Belt", "+205 to maximum Life");
        Assert.NotNull(best);
        Assert.Equal("Prefix", best!.AffixType);
        Assert.Equal(1, best.Tier);
        Assert.True(best.TierCount > 1, "у жизни на поясе больше одного тира");
    }

    [Fact]
    public void LowerRoll_GetsHigherTierNumber()
    {
        var best  = Resolve("Heavy Belt", "+205 to maximum Life");
        var worse = Resolve("Heavy Belt", "+87 to maximum Life");
        Assert.NotNull(best);
        Assert.NotNull(worse);
        Assert.True(worse!.Tier > best!.Tier,
            $"худший ролл должен иметь больший номер тира (best={best.Tier}, worse={worse.Tier})");
        Assert.Equal(best.TierCount, worse.TierCount);
    }

    [Fact]
    public void SuffixIsRecognised()
    {
        var str = Resolve("Heavy Belt", "+14 to Strength");
        Assert.NotNull(str);
        Assert.Equal("Suffix", str!.AffixType);
        Assert.True(str.Tier >= 1);
        // Шаблон с диапазоном нужен редактору для ползунка.
        Assert.Contains("(", str.StatText);
    }

    [Fact]
    public void WeaponLocalMod_ResolvesFromTypeSpecificPool()
    {
        // Локальные моды оружия лежат в data.itemMods.Quarterstaff, а не в .Item —
        // пул должен идти по той же цепочке, что Item.lua.
        var speed = Resolve("Bolting Quarterstaff", "27% increased Attack Speed");
        Assert.NotNull(speed);
        Assert.Equal("Suffix", speed!.AffixType);
        Assert.True(speed.TierCount > 1);
    }

    [Fact]
    public void UnknownMod_ResolvesToNull()
    {
        // Мод не из пула базы: лучше без грейда, чем с выдуманным.
        Assert.Null(Resolve("Heavy Belt", "+3 to Level of all Attack Skills"));
    }

    [Fact]
    public void ResultCount_MatchesInputCount()
    {
        var lines = new List<string>
        {
            "+205 to maximum Life",
            "totally not a mod",
            "+14 to Strength",
        };
        var res = _host.ResolveModTiers("Heavy Belt", lines);
        Assert.Equal(lines.Count, res.Count);
        Assert.NotNull(res[0]);
        Assert.Null(res[1]);
        Assert.NotNull(res[2]);
    }
}
