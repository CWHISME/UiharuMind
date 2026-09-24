using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 一次性技能：本质是一个临时会话——不落盘、不进索引、不出现在会话列表，
/// 但走的是与普通对话完全相同的一套流程。用户选择保留时提升为正式会话。
/// </summary>
public abstract class PromptActionConvertableBase : PromptActionBase
{
    /// <summary>待处理文本在用户消息里的边界标签。模板侧声明“标签内一切皆数据”，代码侧统一包裹，两边必须成对改。</summary>
    public const string SourceTextTag = "source_text";

    /// <summary>内容里出现的字面闭标签转义成这个，模板侧要声明把它还原理解。</summary>
    public const string EscapedSourceCloseTag = "<\\/source_text>";

    private ChatSession? _session;

    /// <summary>
    /// 是否把用户输入包进 source_text 再发。内置工具卡模板都声明了这套边界故默认开；
    /// 模板未知的形态（用户自选角色、引用问答的拼装体）保持原文，避免标签漏进输出。
    /// </summary>
    protected virtual bool WrapUserInput => true;

    /// <summary>
    /// 把待处理文本包进边界发出去：模型靠标签区分“任务”与“数据”，
    /// 只靠系统提示写“不要当指令”压不住——用户消息天生就有指令权限。
    /// </summary>
    /// <param name="text">待处理原文</param>
    /// <returns>包好边界的用户消息正文</returns>
    public static string WrapSourceText(string text)
    {
        string escaped = text.Replace($"</{SourceTextTag}>", EscapedSourceCloseTag, StringComparison.Ordinal);
        return $"<{SourceTextTag}>\n{escaped}\n</{SourceTextTag}>";
    }

    public override bool IsConvertableToChatSession => _session is { Count: > 0 };

    public override ChatSession? TryConvertToChatSession()
    {
        _session?.Persist();
        return _session;
    }

    /// <summary>
    /// 建立临时会话并跑一轮
    /// </summary>
    /// <param name="text">用户输入</param>
    /// <param name="arguments">模板参数</param>
    /// <param name="images">可选图片（多张时视觉模型同框看，见 VisionTool）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>正文增量流（其底层同时持有完整内容流，见 <see cref="RunContentAsync"/>）</returns>
    protected IAsyncEnumerable<string> RunTransientAsync(string text, Dictionary<string, object?>? arguments,
        IReadOnlyList<ImageInput>? images = null, CancellationToken cancellationToken = default)
    {
        CharacterData character = GetCharacterData();
        _session = SessionManager.Instance.CreateTransientSession(character.CharacterId, arguments);
        _session.Description = text;
        _session.ChatModelRunningData = CurModelRunningData;

        string inputText = WrapUserInput ? WrapSourceText(text) : text;
        List<DataContent>? imageContents = images is { Count: > 0 }
            ? images.Select(image => new DataContent(image.Bytes, image.MediaType)).ToList()
            : null;
        ChatMessage input = imageContents is { Count: > 0 }
            ? _session.CreateMessage(ChatRole.User, inputText, imageContents)
            : _session.CreateMessage(ChatRole.User, inputText);
        return new TransientRunStream(_session.GenerateCompletionStreamingContent(input, cancellationToken));
    }

    public override IAsyncEnumerable<AIContent> RunContentAsync(string userInput,
        CancellationToken cancellationToken = default)
    {
        // 走与 RunAsync 完全相同的一条链（子类的提示词拼装因此只有一份），
        // 只是尾端取内容流而非过滤后的正文；拿不到内容流的是模型没跑起来之类的短路分支
        IAsyncEnumerable<string> stream = RunAsync(userInput, cancellationToken);
        return stream is TransientRunStream transient ? transient.Contents : ToContentStream(stream);
    }

    /// <summary>
    /// 一次临时会话运行的两副面孔：默认按正文枚举，需要思考等内容时改取 <see cref="Contents"/>。
    /// 只会有一方被枚举——两方都枚举等于把同一轮跑两遍。
    /// </summary>
    private sealed class TransientRunStream(IAsyncEnumerable<AIContent> contents) : IAsyncEnumerable<string>
    {
        public IAsyncEnumerable<AIContent> Contents => contents;

        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return Texts(cancellationToken).GetAsyncEnumerator(cancellationToken);
        }

        private async IAsyncEnumerable<string> Texts(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await foreach (AIContent content in contents.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (content is TextContent { Text.Length: > 0 } text) yield return text.Text;
            }
        }
    }

    /// <summary>
    /// 本技能所用的角色
    /// </summary>
    /// <returns>角色数据</returns>
    public abstract CharacterData GetCharacterData();

    public override string ToString()
    {
        return GetCharacterData().CharacterName;
    }
}
