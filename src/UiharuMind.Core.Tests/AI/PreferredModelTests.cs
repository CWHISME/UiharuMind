using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 自动挑模型：视觉模型也能聊天，正在用的、收藏的不因为能看图就被跳过；
/// 只有什么都没定时的兜底才优先挑不带视觉的（专门的视觉模型聊天多半偏弱）
/// </summary>
public class PreferredModelTests
{
    private static ModelRunningData Remote(string name, bool vision) =>
        new(new RemoteModelInfo { Config = new RemoteModelConfig { ModelName = name, IsVision = vision } });

    private static string? Pick(ModelRunningData? current, string[] favorites, bool needsVision,
        params ModelRunningData[] all) =>
        PreferredModel.Pick(current, favorites, all.ToDictionary(m => m.ModelName), all, needsVision)?.ModelName;

    [Fact]
    public void ChatRequests_KeepARunningOrFavoriteVisionModel()
    {
        ModelRunningData plain = Remote("plain", false);
        ModelRunningData seeing = Remote("seeing", true);

        Assert.Equal("seeing", Pick(seeing, [], false, plain, seeing));
        Assert.Equal("seeing", Pick(null, ["seeing"], false, plain, seeing));
    }

    [Fact]
    public void Fallback_PrefersAPlainModel_ButTakesAVisionOneWhenThatIsAllThereIs()
    {
        ModelRunningData seeing = Remote("seeing", true);

        Assert.Equal("plain", Pick(null, [], false, seeing, Remote("plain", false)));
        Assert.Equal("seeing", Pick(null, [], false, seeing));
    }

    [Fact]
    public void VisionRequests_StillOnlyTakeVisionModels()
    {
        ModelRunningData plain = Remote("plain", false);

        Assert.Equal("seeing", Pick(plain, ["plain"], true, plain, Remote("seeing", true)));
        Assert.Null(Pick(plain, ["plain"], true, plain));
    }
}
