using PBLApp.Core.Export;
using PBLEngine;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>Экспорт билда в *.build внутриигрового планировщика PoE2 (схема GGG:
/// pathofexile.com/developer/docs/game). Проверяется форма файла и то, что
/// идентификаторы — те же строки, что игра ждёт в PassiveSkills/BaseItemTypes.</summary>
[Collection("LuaHost")]
public class BuildGuideExportTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public BuildGuideExportTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void Guide_HasRequiredNameField()
    {
        var guide = BuildGuideExporter.BuildGuide(_host, "Мой билд", out _);

        Assert.Equal("Мой билд", guide["name"]!.GetValue<string>());
    }

    [Fact]
    public void Guide_EmptyBuildNameFallsBackToPlaceholder()
    {
        var guide = BuildGuideExporter.BuildGuide(_host, "   ", out _);

        Assert.False(string.IsNullOrWhiteSpace(guide["name"]!.GetValue<string>()));
    }

    [Fact]
    public void Guide_AscendancyIsInternalId()
    {
        // Витч → Инфернальная: внутренний id восхождения («Witch1»), а не имя.
        _host.SelectClass(ClassId("Witch"), 1);

        var guide = BuildGuideExporter.BuildGuide(_host, "Ascendancy", out _);

        var ascendancy = guide["ascendancy"]?.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(ascendancy));
        Assert.Matches("^[A-Za-z]+[0-9]$", ascendancy);
    }

    [Fact]
    public void Guide_PassivesUsePassiveSkillsIds()
    {
        _host.SelectClass(ClassId("Witch"), 0);

        var guide = BuildGuideExporter.BuildGuide(_host, "Passives", out var stats);

        // Стартовая нода класса аллоцирована всегда — значит массив непустой,
        // а её id — строка из PassiveSkills, а не число.
        var passives = Assert.IsType<JsonArray>(guide["passives"]);
        Assert.NotEmpty(passives);
        Assert.All(passives, p =>
        {
            var id = p!["id"]!.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.False(int.TryParse(id, out _));
        });
        Assert.Equal(0, stats.SkippedPassives);
    }

    [Fact]
    public void Guide_SkillsCarryGemMetadataIds()
    {
        _host.AddSkillGroupWithGem("Ice Nova");

        var guide = BuildGuideExporter.BuildGuide(_host, "Skills", out _);

        var skills = Assert.IsType<JsonArray>(guide["skills"]);
        var ids = skills.Select(s => s!["id"]!.GetValue<string>()).ToList();
        Assert.Contains("Metadata/Items/Gems/SkillGemIceNova", ids);
    }

    [Fact]
    public void Export_WritesFileWithBuildExtension()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pbl-guide-" + Path.GetRandomFileName());
        try
        {
            var result = BuildGuideExporter.Export(_host, "Guide: test/build", folder);

            Assert.True(result.Ok, result.Error);
            Assert.True(File.Exists(result.FilePath));
            Assert.Equal(".build", Path.GetExtension(result.FilePath));
            // Имя файла очищено от символов, которые Windows не пускает в путь.
            Assert.DoesNotContain('/', Path.GetFileName(result.FilePath!));

            var parsed = JsonNode.Parse(File.ReadAllText(result.FilePath!));
            Assert.NotNull(parsed?["name"]);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private int ClassId(string name) =>
        _host.GetClassData().First(c => c.Name == name).Id;

    /// <summary>Настоящий билд сообщества целиком: гайд должен получить все четыре
    /// раздела — восхождение, пассивки, умения и слоты снаряжения.</summary>
    [Fact]
    public void Guide_RealCommunityBuild_FillsEverySection()
    {
        var xml = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "parity",
            "community_builds", "3MrEDKwx_dgkAPFvLi4Pc.xml"));
        _host.LoadBuildFromXml(xml, "Community build");

        var guide = BuildGuideExporter.BuildGuide(_host, "Community build", out var stats);

        Assert.False(string.IsNullOrEmpty(guide["ascendancy"]?.GetValue<string>()));
        Assert.True(stats.Passives > 50, $"пассивок в гайде: {stats.Passives}");
        Assert.Equal(0, stats.SkippedPassives);
        Assert.True(stats.Skills > 0, "ни одного умения в гайде");
        Assert.True(stats.Items > 0, "ни одного слота снаряжения в гайде");

        static JsonArray slotsOf(JsonObject g) => (JsonArray)g["inventory_slots"]!;

        // Уник назван именем из таблицы Words: PoB хранит его вместе с базой
        // («Mageblood, Utility Belt»), игре нужен только заголовок.
        var uniques = slotsOf(guide).Where(s => s!["unique_name"] is not null)
            .Select(s => s!["unique_name"]!.GetValue<string>()).ToList();
        Assert.Contains("Mageblood", uniques);
        Assert.All(uniques, u => Assert.DoesNotContain(",", u));

        // Слоты названы идентификаторами таблицы Inventories, а не именами PoB.
        var slots = Assert.IsType<JsonArray>(guide["inventory_slots"]);
        Assert.All(slots, slot =>
        {
            var id = slot!["inventory_id"]!.GetValue<string>();
            Assert.DoesNotContain(' ', id);
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repo root (no src/ ancestor)");
    }
}
