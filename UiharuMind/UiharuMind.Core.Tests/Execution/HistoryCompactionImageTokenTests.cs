using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死图片 token 的修正。框架把非文本内容一律按「字节数 / 4」估，一张 150KB 的截图会被
/// 算成 3.7 万 token——虚高二三十倍，后果是压缩被凭空提前触发去砍真实对话。
///
/// 这里同时钉住三件事：修正后的量级对得上真实计价、非图片内容不受影响、
/// 以及每组修正值非负——最后一条是承重的：截断策略靠「排除一组再重问一次条件」收敛，
/// 一旦某组修正后为负，排除它反而让总数变大，截断就永远停不下来。
/// </summary>
public class HistoryCompactionImageTokenTests
{
    private const string ImageType = "image/png";

    [Fact]
    public void PerImageCeiling_IsDerivedFromMaxEdge()
    {
        //上界必须是从长边上限推出来的,不能是另写一个字面量:改了尺寸而计价没跟上就会低估
        Assert.Equal(InlineImageLimits.MaxEdge * InlineImageLimits.MaxEdge / 750,
            InlineImageLimits.MaxTokensPerImage);
        Assert.Equal(3278, InlineImageLimits.MaxTokensPerImage);
    }

    [Fact]
    public void GroupWithoutImages_KeepsFrameworkCount()
    {
        List<ChatMessage> messages = [new(ChatRole.User, "没有图片的一条消息")];

        //不含图片时不作任何假设,原样采用框架的数
        Assert.Equal(1234, HistoryCompaction.CorrectedGroupTokens(4936, 1234, messages));
    }

    [Fact]
    public void GroupWithImage_CollapsesToCeilingInsteadOfBytesOverFour()
    {
        const int imageBytes = 150 * 1024;
        List<ChatMessage> messages = [ImageMessage("看看这张", imageBytes)];
        (int groupBytes, int groupTokens) = FrameworkCountsOf(messages);

        long corrected = HistoryCompaction.CorrectedGroupTokens(groupBytes, groupTokens, messages);

        //框架估成 3.8 万,修正后是「正文字节/4 + 一张图的上界」
        Assert.True(groupTokens > 38_000, $"框架的估算应当虚高,实际 {groupTokens}");
        Assert.Equal(InlineImageLimits.MaxTokensPerImage + TextBytesOf("看看这张") / 4, corrected);
        Assert.True(corrected < groupTokens / 10, "修正后应当降一个量级");
    }

    [Fact]
    public void MultipleImages_CountOncePerImage()
    {
        const int imageBytes = 100 * 1024;
        List<ChatMessage> messages =
        [
            ImageMessage("三张图", imageBytes, imageBytes, imageBytes),
        ];
        (int groupBytes, int groupTokens) = FrameworkCountsOf(messages);

        long corrected = HistoryCompaction.CorrectedGroupTokens(groupBytes, groupTokens, messages);

        Assert.Equal(3 * InlineImageLimits.MaxTokensPerImage + TextBytesOf("三张图") / 4, corrected);
    }

    [Fact]
    public void NonImageAttachments_AreLeftToTheFramework()
    {
        //只有图片有「按像素计价」这个可证的上界;别的二进制附件仍按框架的字节估算走
        List<ChatMessage> messages =
        [
            new(ChatRole.User, [new DataContent(new byte[50 * 1024], "application/pdf")]),
        ];
        (int groupBytes, int groupTokens) = FrameworkCountsOf(messages);

        Assert.Equal(groupTokens, HistoryCompaction.CorrectedGroupTokens(groupBytes, groupTokens, messages));
    }

    [Fact]
    public void CorrectedGroup_IsNeverNegative()
    {
        //整组几乎全是图片字节的极端情况:修正值仍须为正,否则排除该组会让总数变大,截断停不下来
        List<ChatMessage> messages = [new(ChatRole.User, [new DataContent(new byte[2 * 1024 * 1024], ImageType)])];
        (int groupBytes, int groupTokens) = FrameworkCountsOf(messages);

        Assert.True(HistoryCompaction.CorrectedGroupTokens(groupBytes, groupTokens, messages) > 0);
    }

    /// <summary>
    /// 按框架的口径算出一组消息的字节数与 token 数
    /// （<c>CompactionMessageIndex.CreateGroup</c>：无 tokenizer 时是「整组字节 ÷ 4」除一次）
    /// </summary>
    /// <param name="messages">消息</param>
    /// <returns>字节数与 token 数</returns>
    private static (int Bytes, int Tokens) FrameworkCountsOf(IReadOnlyList<ChatMessage> messages)
    {
        int bytes = 0;
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                bytes += content switch
                {
                    DataContent data => data.Data.Length + TextBytesOf(data.MediaType),
                    TextContent text => TextBytesOf(text.Text),
                    _ => 0,
                };
            }
        }

        return (bytes, bytes / 4);
    }

    private static int TextBytesOf(string? value)
    {
        return string.IsNullOrEmpty(value) ? 0 : System.Text.Encoding.UTF8.GetByteCount(value);
    }

    private static ChatMessage ImageMessage(string text, params int[] imageSizes)
    {
        List<AIContent> contents = [new TextContent(text)];
        foreach (int size in imageSizes) contents.Add(new DataContent(new byte[size], ImageType));
        return new ChatMessage(ChatRole.User, contents);
    }
}
