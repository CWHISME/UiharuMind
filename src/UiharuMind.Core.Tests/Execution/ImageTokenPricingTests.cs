using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死图片计价的口径选择。默认口径是「最贵的一家 × 最大的尺寸」推出的天花板，
/// 拿它当通行价去计 DeepSeek（每张封顶 1024 且与尺寸无关）就是几倍高估——
/// 而高估不会被校准纠回来（那个系数只向上调），只会让压缩提前动手、缓存提前断。
/// </summary>
public class ImageTokenPricingTests
{
    [Theory]
    [InlineData("(OCG)DeepSeek V4.1 Flash")]
    [InlineData("deepseek-flash")]
    [InlineData("DeepSeek-VL2")]
    public void For_RecognizesDeepSeek_EvenBehindAChannelPrefix(string modelName)
    {
        Assert.Same(ImageTokenPricing.DeepSeek, ImageTokenPricing.For(modelName));
    }

    [Theory]
    [InlineData("claude-sonnet-4")]
    [InlineData("(CCG)Anthropic Claude Opus")]
    public void For_RecognizesAnthropic_WhichDownscalesBeforeCharging(string modelName)
    {
        //超过 1568 它自己会缩再计,按发送尺寸算会凭空多三成
        Assert.Same(ImageTokenPricing.Anthropic, ImageTokenPricing.For(modelName));
        Assert.Equal(3278, ImageTokenPricing.Anthropic.MaxTokens);
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("glm-5")]
    [InlineData(null)]
    [InlineData("")]
    public void For_LeavesUnknownFamiliesOnTheDefaultSide(string? modelName)
    {
        //口径不认识就留在偏高的一侧:宁可早压,不可超窗
        Assert.Same(ImageTokenPricing.Default, ImageTokenPricing.For(modelName));
    }

    [Fact]
    public void Default_IsCappedByTheSendEdge_NotAnthropicsEdge()
    {
        //认不出的服务商可能不缩图、按像素收,所以上界按**发出去**的尺寸推
        Assert.Equal(InlineImageLimits.SendEdge * InlineImageLimits.SendEdge / InlineImageLimits.TokenDivisor,
            ImageTokenPricing.Default.MaxTokens);
        Assert.True(ImageTokenPricing.Default.MaxTokens > ImageTokenPricing.Anthropic.MaxTokens);
        //方形图才看得出两者的差别:16:9 缩到 1568 之后是同一张
        Assert.True(ImageTokenPricing.Default.Estimate(1792, 1792) > ImageTokenPricing.Anthropic.Estimate(1792, 1792));
    }

    [Theory]
    [InlineData(4000, 4000)]
    [InlineData(1568, 882)]
    [InlineData(256, 256)]
    public void DeepSeek_ChargesItsCeilingRegardlessOfSize(int width, int height)
    {
        //官方:缩到总像素约 1300×1300 再计,2000×2000 与 5000×5000 同价,上限 1024
        Assert.Equal(1024, ImageTokenPricing.DeepSeek.Estimate(width, height));
    }

    [Fact]
    public void DeepSeek_DoesNotEvenReadTheHeader()
    {
        //与尺寸无关,读不出尺寸也还是那个上界
        Assert.Equal(1024, ImageTokenPricing.DeepSeek.Estimate(new byte[64]));
        Assert.Equal(1024, ImageTokenPricing.DeepSeek.Estimate(TestImages.Png(3, 3)));
    }

    [Fact]
    public void Default_StillDerivesFromMaxEdge()
    {
        Assert.Equal(88, ImageTokenPricing.Default.Estimate(256, 256)); //256²/750 向上取整
        Assert.Equal(InlineImageLimits.MaxTokensPerImage, ImageTokenPricing.Default.Estimate(4000, 4000));
        Assert.Equal(1844, ImageTokenPricing.Default.Estimate(1568, 882)); //已成等比,不必再缩
    }

    [Fact]
    public void Default_IsTheMoreExpensiveSide()
    {
        //同一张 1568×882 的预览:默认口径 1844,DeepSeek 1024——这就是被修掉的那 1.8 倍
        int standard = ImageTokenPricing.Default.Estimate(1568, 882);
        Assert.Equal(1844, standard);
        Assert.Equal(1024, ImageTokenPricing.DeepSeek.Estimate(1568, 882));
        Assert.True(standard > ImageTokenPricing.DeepSeek.Estimate(1568, 882));
    }

    [Fact]
    public void Estimate_FollowsTheModelDeclaredForTheTurn()
    {
        TurnInputEstimate estimate = new();
        Assert.Same(ImageTokenPricing.Default, estimate.ImagePricing); //没声明过

        estimate.UseModel("(OCG)DeepSeek V4.1 Flash");
        Assert.Same(ImageTokenPricing.DeepSeek, estimate.ImagePricing);

        estimate.UseModel("gpt-4o");
        Assert.Same(ImageTokenPricing.Default, estimate.ImagePricing);
    }

    [Fact]
    public void Detached_HonoursTheServingModel()
    {
        //旁路请求发的是同一份历史,图片口径必须与主链一致,否则两边各算一个水位
        Assert.Same(ImageTokenPricing.DeepSeek, TurnInputEstimate.Detached(0, "deepseek-flash").ImagePricing);
        Assert.Same(ImageTokenPricing.Default, TurnInputEstimate.Detached(0).ImagePricing);
    }
}
