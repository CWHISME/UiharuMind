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

    /// <summary>
    /// 反序列化期间的 setter 不应把半成品写盘：种子（含尾部字段 <c>FavoriteCharacterIds</c> /
    /// <c>SettingsScrollPositions</c>）放临时文件，Load 后内存正确，且规范路径保持原样。
    /// 曾出现：配置类的属性 setter 带 <see cref="ConfigBase.Save"/>，STJ 按 JSON 键顺序逐个调 setter，
    /// 排在带 Save 的 setter 之后的普通字段还没被赋值时，半成品写盘把磁盘上的收藏覆盖成空
    /// （重启丢收藏的根因，抑制机制见 <see cref="SaveUtility.SaveSuppressed"/>）。
    /// 注意不断言种子文件本身：<see cref="ConfigBase.Save"/> 的目标永远是规范路径（按类型名算，
    /// 与从哪 Load 无关），种子放临时文件、修之前照样通过，是空测试；必须看规范路径。
    /// </summary>
    [Fact]
    public void Load_PreservesDiskContent_WhenSetterSavesHalfWrittenConfig()
    {
        string canonical = AppPaths.Config.ForType(nameof(SettingConfig));
        Directory.CreateDirectory(AppPaths.Config.Root);
        // 快乐路径下对用户配置零写入：只读不断言之外的任何修改。
        // 抑制机制若失效，Load 会弄脏规范路径（断言变红），finally 负责恢复。
        byte[]? backup = File.Exists(canonical) ? File.ReadAllBytes(canonical) : null;
        string seedPath = Path.Combine(Path.GetTempPath(), $"save-half-baked-seed-{Guid.NewGuid():N}.json");
        const string seed = """
            {
              "IsLocalServer": true,
              "ClipboardRetentionDays": 0,
              "LanguageCode": "zh-CN",
              "ThemeMode": "Dark",
              "CharacterFilterIndex": 0,
              "CharacterOriginFilter": 0,
              "CharacterFavoriteFilter": false,
              "FavoriteCharacterIds": ["built-in-id", "user-guid"],
              "SettingsScrollPositions": {"page": 42}
            }
            """;
        File.WriteAllText(seedPath, seed);
        try
        {
            SettingConfig? loaded = SaveUtility.Load<SettingConfig>(seedPath);

            Assert.NotNull(loaded);
            Assert.Equal("Dark", loaded!.ThemeMode);
            Assert.Equal(new[] { "built-in-id", "user-guid" }, loaded.FavoriteCharacterIds);
            Assert.Equal(42d, Assert.Single(loaded.SettingsScrollPositions).Value);
            byte[]? after = File.Exists(canonical) ? File.ReadAllBytes(canonical) : null;
            bool untouched = (backup is null && after is null)
                || (backup is not null && after is not null && backup.SequenceEqual(after));
            Assert.True(untouched, "Load 不应触碰规范路径上的配置文件");
        }
        finally
        {
            if (File.Exists(seedPath)) File.Delete(seedPath);
            if (backup is null)
            {
                if (File.Exists(canonical)) File.Delete(canonical);
            }
            else if (!File.ReadAllBytes(canonical).SequenceEqual(backup)) File.WriteAllBytes(canonical, backup);
        }
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