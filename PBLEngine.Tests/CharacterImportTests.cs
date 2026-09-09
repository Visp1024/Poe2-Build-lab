using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>Мост C# → Lua-ImportTab: LuaHost.ImportCharacter на синтетическом
/// ответе API персонажей (форма как у /character/poe2/&lt;имя&gt;).</summary>
[Collection("LuaHost")]
public class CharacterImportTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public CharacterImportTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    private const string Template =
        """
        {"character":{
          "name":"TestChar","class":"@CLASS@","level":@LEVEL@,"league":"Standard",
          "passives":{"hashes":[],"specialisations":{},"skill_overrides":{},
                      "quest_stats":[],"jewel_data":{},"mastery_effects":{}},
          "jewels":[], "equipment":[], "skills":[]
        }}
        """;

    private static string CharacterJson(string cls = "Warrior", int level = 42) =>
        Template.Replace("@CLASS@", cls).Replace("@LEVEL@", level.ToString());

    [Fact]
    public void ImportCharacter_PassiveTree_AppliesClassAndLevel()
    {
        var result = _host.ImportCharacter(CharacterJson(level: 42),
            new CharacterImportOptions { PassiveTree = true, ItemsAndSkills = false });

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Warrior", _host.GetCurrentClassInfo().ClassName);
        Assert.Equal(42, _host.GetCharacterLevel());
    }

    [Fact]
    public void ImportCharacter_ItemsAndSkills_EmptyPayloadClearsBuild()
    {
        // Пустое снаряжение с включённой очисткой — билд должен остаться валидным,
        // а не упасть на отсутствующих полях.
        var result = _host.ImportCharacter(CharacterJson(),
            new CharacterImportOptions { PassiveTree = false, ItemsAndSkills = true });

        Assert.True(result.Ok, result.Error);
        Assert.Empty(_host.GetEquippedItems());
    }

    [Fact]
    public void ImportCharacter_AcceptsBareCharacterObject()
    {
        var wrapped = CharacterJson();
        // Тот же объект без обёртки {"character": …}
        var bare = wrapped["{\"character\":".Length..^1];

        var result = _host.ImportCharacter(bare,
            new CharacterImportOptions { PassiveTree = true, ItemsAndSkills = false });

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void ImportCharacter_NothingSelected_IsANoOp()
    {
        var result = _host.ImportCharacter("not json at all",
            new CharacterImportOptions { PassiveTree = false, ItemsAndSkills = false });

        Assert.True(result.Ok);
    }

    [Fact]
    public void ImportCharacter_MalformedJson_ReportsError()
    {
        var result = _host.ImportCharacter("{ this is not json", new CharacterImportOptions());

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ImportCharacter_ResponseWithoutCharacter_ReportsError()
    {
        var result = _host.ImportCharacter("""{"error":{"code":1,"message":"Forbidden"}}""",
            new CharacterImportOptions());

        Assert.False(result.Ok);
        Assert.Contains("персонаж", result.Error!);
    }

    [Fact]
    public void ImportCharacter_RecordsBindingForReimport()
    {
        var result = _host.ImportCharacter(CharacterJson(),
            new CharacterImportOptions { PassiveTree = true, ItemsAndSkills = false });
        Assert.True(result.Ok, result.Error);

        var binding = _host.GetCharacterBinding();
        Assert.Equal("TestChar", binding.CharacterName);
        Assert.True(binding.HasValue);
        // Хеш пишется рядом с именем — по нему персонажа узнаёт оригинальный PoB.
        Assert.Equal(_host.Sha1("TestChar"), binding.Hash);
    }

    [Fact]
    public void CharacterBinding_SurvivesSaveAndLoad()
    {
        _host.ImportCharacter(CharacterJson(),
            new CharacterImportOptions { PassiveTree = true, ItemsAndSkills = false });
        _host.SetCharacterBinding("TestChar", "TestAccount");

        var xml = _host.SaveBuildToXml();
        Assert.NotNull(xml);
        _host.NewBuild();
        Assert.Null(_host.GetCharacterBinding().CharacterName);

        _host.LoadBuildFromXml(xml!, "Reloaded");

        var binding = _host.GetCharacterBinding();
        Assert.Equal("TestChar", binding.CharacterName);
        Assert.Equal("TestAccount", binding.AccountName);
    }
}
