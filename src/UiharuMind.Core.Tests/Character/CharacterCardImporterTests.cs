using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.AI.WorldSettings;

namespace UiharuMind.Core.Tests.Character;

public class CharacterCardImporterTests
{
    [Fact]
    public async Task ImportToCharactorData_MapsNewFields()
    {
        const string json = """
        {
          "spec": "chara_card_v2",
          "spec_version": "2.0",
          "data": {
            "name": "测试角色",
            "alternate_greetings": ["开场白A", "开场白B"],
            "post_history_instructions": "不要替用户说话。",
            "extensions": {
              "depth_prompt": { "prompt": "记住：你在学园都市。", "depth": 2, "role": "system" }
            },
            "character_book": {
              "name": "学园都市",
              "entries": [
                { "keys": ["学园都市"], "content": "第七学区。" },
                { "keys": ["御坂美琴"], "content": "超电磁炮。", "position": "after_char", "insertion_order": 5 },
                { "keys": ["旧条目"], "content": "不应导入", "enabled": false }
              ]
            }
          }
        }
        """;

        CharacterData? character = await CharacterCardImporter.ImportToCharactorData(json);

        Assert.NotNull(character);
        Assert.Equal(["开场白A", "开场白B"], character.AlternateGreetings);
        // post_history_instructions 不设独立字段：并进提示词末尾（见 ADR 0070 实现说明）
        Assert.Contains("不要替用户说话。", character.Template);
        Assert.NotNull(character.DepthPrompt);
        Assert.Equal(2, character.DepthPrompt.Depth);
        Assert.Equal("system", character.DepthPrompt.Role);

        Assert.Equal("测试角色·世界书", character.WorldSettingName);
        WorldSetting? book = WorldSettingManager.Instance.Get(character.WorldSettingName);
        Assert.NotNull(book);
        Assert.Equal(2, book.Entries.Count);
        WorldSettingEntry first = book.Entries[0];
        Assert.Equal(["学园都市"], first.Keys);
        Assert.Equal(EWorldSettingPosition.BeforeCharacter, first.Position);
        WorldSettingEntry second = book.Entries[1];
        Assert.Equal(EWorldSettingPosition.AfterCharacter, second.Position);
        Assert.Equal(5, second.Order);
    }

    [Fact]
    public async Task ImportToCharactorData_NoBook_LeavesEmptyWorldSetting()
    {
        const string json = """
        {
          "spec": "chara_card_v2",
          "data": { "name": "无书角色" }
        }
        """;

        CharacterData? character = await CharacterCardImporter.ImportToCharactorData(json);

        Assert.NotNull(character);
        Assert.Equal("", character.WorldSettingName);
        Assert.Null(character.DepthPrompt);
        Assert.Empty(character.AlternateGreetings);
    }

    [Fact]
    public async Task Import_AlternateGreetingsWithJunk_DropsNonStrings()
    {
        // 乱写的卡：数组里混数字/对象/布尔，不该让整张导入失败
        const string json = """
        {
          "spec": "chara_card_v2",
          "data": {
            "name": "乱写卡",
            "alternate_greetings": ["正常开场白", 123, { "x": 1 }, true, "另一个"]
          }
        }
        """;

        CharacterData? character = await CharacterCardImporter.ImportToCharactorData(json);

        Assert.NotNull(character);
        Assert.Equal(["正常开场白", "另一个"], character.AlternateGreetings);
    }

    [Fact]
    public async Task Import_CharacterBookDefaults_PositionAndOrder()
    {
        const string json = """
        {
          "spec": "chara_card_v2",
          "data": {
            "name": "默认值卡",
            "character_book": {
              "entries": [
                { "keys": ["甲"], "content": "A", "position": "before_char" },
                { "keys": ["乙"], "content": "B" }
              ]
            }
          }
        }
        """;

        CharacterData? character = await CharacterCardImporter.ImportToCharactorData(json);

        Assert.NotNull(character);
        // position 非 after_char 一律回落 before；insertion_order 缺省取索引 i
        WorldSetting? book = WorldSettingManager.Instance.Get("默认值卡·世界书");
        Assert.NotNull(book);
        Assert.Equal(EWorldSettingPosition.BeforeCharacter, book.Entries[0].Position);
        Assert.Equal(0, book.Entries[0].Order);
        Assert.Equal(EWorldSettingPosition.BeforeCharacter, book.Entries[1].Position);
        Assert.Equal(1, book.Entries[1].Order);
    }
}