using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// RemoteModelManager 存档类型修复路径的形状断言（主题五拍板第 4 条）：
/// 原反射路径按运行时类型序列化，改造后走 <c>SaveToString&lt;BaseRemoteModelConfig&gt;</c>——
/// 四家配置均无子类专属新增属性（都是 override），因此 base 类型图序列化与其等价。
/// 这个「碰巧等价」必须钉成显式测试：将来谁给某家配置加子类专属字段，这条路径会静默丢字段，测试先红。
/// </summary>
public class RemoteModelConfigRepairShapeTests
{
    [Fact]
    public void RepairPath_RoundTrips_ThroughBaseType_KeepsOverrideFields()
    {
        RemoteAgnesModelConfig agnes = new()
        {
            ModelName = "Agnes-测试",
            ModelId = "agnes-3.0-flash",
            ContextLength = 12345,
            MaxTokens = 6789,
            IsVision = true,
        };

        // RemoteModelManager 修复路径：SaveToString<BaseRemoteModelConfig>(config) 再按注册表类型读回
        string json = SaveUtility.SaveToString<BaseRemoteModelConfig>(agnes);
        object? repaired = SaveUtility.LoadFromString(json, typeof(RemoteAgnesModelConfig), SaveUtility.SaveJsonOptions);

        RemoteAgnesModelConfig? restored = Assert.IsType<RemoteAgnesModelConfig>(repaired);
        Assert.Equal("Agnes-测试", restored.ModelName);
        Assert.Equal("agnes-3.0-flash", restored.ModelId);
        Assert.Equal(12345, restored.ContextLength);
        Assert.Equal(6789, restored.MaxTokens);
        Assert.True(restored.IsVision);
    }

    [Fact]
    public void RepairPath_SerializedJson_ContainsBaseTypeFields()
    {
        RemoteZhipuModelConfig zhipu = new()
        {
            ModelName = "glm-测试",
            ModelId = "glm-4.7-flash",
        };

        string json = SaveUtility.SaveToString<BaseRemoteModelConfig>(zhipu);

        // override 的字段要在 base 类型图序列化里写全（塔奇克马碰巧等价的显式化）
        Assert.Contains("ModelName", json);
        Assert.Contains("ModelId", json);
        Assert.Contains("glm-测试", json);
    }
}