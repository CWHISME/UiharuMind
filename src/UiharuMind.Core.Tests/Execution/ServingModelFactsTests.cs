using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死「按真正发这一发的那个模型算」：钉选的本地模型没在跑时请求会回落全局，
/// 而本地模型没加载时的窗口是 <see cref="ModelContextResolver.LocalFallback"/>（8192）。
/// 照钉选名算压缩预算，等于按一个根本不发请求的窗口在算——轻会话每轮被过度折叠，
/// 工具重的会话额度算成 0 直接不压。
/// </summary>
public class ServingModelFactsTests
{
    private static ModelRunningData Local() => new(new GGufModelInfo { ModelName = "local" });

    private static ModelRunningData Remote(string name, int contextLength, bool seesImages) =>
        new(new RemoteModelInfo
        {
            Config = new RemoteModelConfig { ModelName = name, ContextLength = contextLength, IsVision = seesImages },
        });

    [Fact]
    public void ContextLength_FollowsTheModelThatActuallyServes()
    {
        ModelRunningData local = Local();
        ModelRunningData global = Remote("global", 128_000, seesImages: true);

        //钉着的本地模型没在跑（本地一次只跑一个）→ 请求回落全局
        Assert.Equal(128_000, ServingModelFacts.ContextLength(local, global));
        Assert.Equal(ModelContextResolver.LocalFallback, ServingModelFacts.ContextLength(local, null)); //没有全局可回:就是它自己
    }

    [Fact]
    public void ContextLength_RemotePinIsItsOwn()
    {
        ModelRunningData pinned = Remote("pinned", 1_000_000, seesImages: false);
        ModelRunningData global = Remote("global", 128_000, seesImages: true);

        Assert.Equal(1_000_000, ServingModelFacts.ContextLength(pinned, global)); //远程钉选不会被全局顶掉
    }

    [Fact]
    public void ContextLength_NoModelAtAll_IsZero()
    {
        Assert.Equal(0, ServingModelFacts.ContextLength(null, null)); //0 = 未知,压缩据此不压
    }

    /// <summary>视觉那条用的是同一个解析，别再各写一套</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SeesImages_FollowsTheSameModel(bool globalSeesImages)
    {
        ModelRunningData local = Local();
        ModelRunningData global = Remote("global", 128_000, globalSeesImages);

        Assert.Equal(globalSeesImages, ServingModelFacts.SeesImages(local, global)); //回落全局
        Assert.False(ServingModelFacts.SeesImages(local, null)); //本地没加载:它自己看不了图
        Assert.Equal(globalSeesImages, ServingModelFacts.SeesImages(null, global)); //跟随全局
    }
}
