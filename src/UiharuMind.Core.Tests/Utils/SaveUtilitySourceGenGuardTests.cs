using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 主题五 A2 的验收护栏：SaveUtility 有界类型 source-gen 化后，形状与语义必须与反射基线一致。
/// 覆盖拍板清单：角色卡 V3 导入往返 + 未知字段不炸、记忆往返、ConfigBase 一族对象路由形状回归
/// （防止以后有人手滑把 ConfigBase 切到 source-gen 导致派生字段静默丢）。
/// </summary>
public class SaveUtilitySourceGenGuardTests
{
    [Fact]
    public void CharacterCard_V3_Import_RoundTrips_WithObjectList_AndUnknownFields()
    {
        CharacterCard card = new()
        {
            Spec = "chara_card_v3",
            SpecVersion = "v3",
            Name = "测试卡",
            Data = new Data
            {
                Name = "测试",
                FirstMes = "你好",
                AlternateGreetings = new List<object> { "第一条问候", 42, true },
            },
        };

        string json = SaveUtility.SaveToString<CharacterCard>(card);
        CharacterCard restored = SaveUtility.LoadFromString<CharacterCard>(json, SaveUtility.SaveJsonOptions);

        Assert.Equal("chara_card_v3", restored.Spec);
        Assert.Equal("测试", restored.Data?.Name);
        Assert.Equal("你好", restored.Data?.FirstMes);
        // List<object> 宽容字段：还原成 JsonNode（SaveUtility.SaveJsonOptions 带 UnknownTypeHandling=JsonNode，反射基线即 JsonValue）
        Assert.NotNull(restored.Data?.AlternateGreetings);
        Assert.Equal(3, restored.Data!.AlternateGreetings!.Count);
        JsonValue first = Assert.IsAssignableFrom<JsonValue>(restored.Data.AlternateGreetings[0]);
        Assert.Equal("第一条问候", first.GetValue<string>());
        Assert.IsAssignableFrom<JsonValue>(restored.Data.AlternateGreetings[1]);
        Assert.IsAssignableFrom<JsonValue>(restored.Data.AlternateGreetings[2]);

        // 未知字段（外部角色卡带扩展）不炸
        string jsonWithUnknown = """
            {
              "spec": "chara_card_v3",
              "data": {
                "name": "测试",
                "alternate_greetings": ["hi"],
                "x_unknown_deep": { "a": 1 }
              },
              "x_top_unknown": 123
            }
            """;
        CharacterCard tolerant = SaveUtility.LoadFromString<CharacterCard>(jsonWithUnknown, SaveUtility.SaveJsonOptions);
        Assert.Equal("测试", tolerant.Data?.Name);
        Assert.Single(tolerant.Data!.AlternateGreetings!);
    }

    [Fact]
    public void CharacterData_RoundTrips_ViaSourceGen_KeepsFields()
    {
        CharacterData data = new()
        {
            CharacterId = "c1",
            CharacterName = "阿洛娜",
            Description = "测试描述",
            IsAgent = true,
            MemoryName = "notes",
        };

        string json = SaveUtility.SaveToString<CharacterData>(data);
        CharacterData restored = SaveUtility.LoadFromString<CharacterData>(json, SaveUtility.SaveJsonOptions);

        Assert.Equal("c1", restored.CharacterId);
        Assert.Equal("阿洛娜", restored.CharacterName);
        Assert.Equal("测试描述", restored.Description);
        Assert.True(restored.IsAgent);
        Assert.Equal("notes", restored.MemoryName);
    }

    [Fact]
    public void MemoryData_RoundTrips_ViaSourceGen_KeepsFields()
    {
        MemoryData memory = new()
        {
            Name = "知识库",
            Description = "描述",
            IndexDirty = true,
        };

        string json = SaveUtility.SaveToString<MemoryData>(memory);
        MemoryData restored = SaveUtility.LoadFromString<MemoryData>(json, SaveUtility.SaveJsonOptions);

        Assert.Equal("知识库", restored.Name);
        Assert.Equal("描述", restored.Description);
        Assert.True(restored.IndexDirty);
    }

    /// <summary>设置经基类保存（<see cref="ConfigBase.Save"/> 的路子）时按运行时类型写，派生字段不能丢</summary>
    [Fact]
    public void ConfigBase_SavedThroughBase_KeepsDerivedFields()
    {
        SettingConfig setting = new()
        {
            IsLocalServer = false,
            DictationHoldKey = "HoldX",
            FavoriteCharacterIds = { "a", "b" },
        };

        string path = Path.Combine(Path.GetTempPath(), $"save-config-guard-{Guid.NewGuid():N}.json");
        try
        {
            ConfigBase asBase = setting;
            SaveUtility.Save(path, asBase);
            SettingConfig? restored = SaveUtility.Load<SettingConfig>(path);

            Assert.NotNull(restored);
            Assert.False(restored!.IsLocalServer);
            Assert.Equal("HoldX", restored.DictationHoldKey);
            Assert.Equal(new[] { "a", "b" }, restored.FavoriteCharacterIds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}