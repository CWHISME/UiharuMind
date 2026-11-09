using System.Text;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 流式 &lt;think&gt; 标签解析器:把本地/远程模型混在正文流里的思考段与正文分离。
/// 标签可能被流式增量任意切断(如 "&lt;thi" + "nk&gt;"),
/// 末尾疑似半个标签的部分会被扣留,待后续增量或 <see cref="Complete"/> 时定夺。
///
/// <para><b>开标签只在本段还没吐出过可见正文时算数</b>(允许前导空白):一旦已经有正文上屏,
/// 后面出现的 &lt;think&gt; 一律按字面量。理由是这种控制标签与「正文里引用它的字符串」在字节上
/// <b>无从区分</b>——模型答一句「`&lt;think&gt;` 混排的 400 条…」就会被当成真的开标签,
/// 把它之后的所有正文吞进思考卡(实机:一条 5620 字的回答被切在第 829 字,后 4784 字成了「思考」,
/// 回放时这段字数还会据此计进存档;同一轮里标签被引用几十次,就是回答被切成几十块)。
/// 真正的内联思考总在消息开头,所以这条规则不会误伤它;反过来,正文中间真出现第二段内联思考时
/// 它会按正文显示——不丢内容,只是不折叠,取舍是「宁可多显示,不要吞掉正文」。</para>
///
/// <para><b>框架已经给出结构化思考（<c>TextReasoningContent</c>）时不再解析标签</b>：
/// 那一发的思考已由独立字段表达，同一段正文里再出现 <c>&lt;think&gt;</c> 只能是引用。
/// 由 <see cref="ThinkingBoundary"/> 分发结构化思考时调 <see cref="DisableInlineTags"/>，
/// <see cref="Reset"/> 时恢复。少了这道，模型讨论标签解析器的回答会被自己的引用切成好几块。</para>
///
/// <para><b>不从文本猜「这段正文其实是不是思考」</b>：推理续句与正常回答在字节上无从区分，
/// 上游把推理流切进 <c>content</c> 时，SDK 给出的类型就是唯一事实。宁可把续句按正文显示，
/// 也不能据此把整段回答折叠进思考卡（实机：一份总结报告被整段吞掉）。</para>
///
/// <para>段边界由 <see cref="ThinkingBoundary.Dispatch"/> 统一收口并冲刷扣留内容（只收段）；
/// 只有新的一发、切换会话或整段重发才走 <see cref="Reset"/> 重新判定要不要解析内联标签。
/// 「本发已给出结构化思考」是调用级事实，工具调用与迟到的落盘信号都不该把它抹掉。</para>
/// </summary>
public class ThinkTagStreamParser
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    private readonly StringBuilder _pending = new();
    private bool _inThinking;
    private bool _tagAllowed = true; //本段还没吐出过可见正文:此时开标签才算标签
    private bool _inlineTagsEnabled = true; //框架给出结构化思考后，正文里的标签只能是引用

    /// <summary>
    /// 喂入一段流式增量,按当前所在区段把可确定的部分分发给正文或思考回调
    /// </summary>
    /// <param name="delta">增量文本</param>
    /// <param name="onText">正文回调</param>
    /// <param name="onThinking">思考回调</param>
    public void Feed(string delta, Action<string> onText, Action<string> onThinking)
    {
        if (string.IsNullOrEmpty(delta)) return;
        // 框架已把这一发的思考放进独立字段：正文里的标签只能是引用，连半截标签也不必扣留
        if (!_inlineTagsEnabled)
        {
            string buffered = _pending.ToString() + delta;
            _pending.Clear();
            _inThinking = false;
            onText(buffered);
            return;
        }

        _pending.Append(delta);

        while (true)
        {
            string buffer = _pending.ToString();
            //已有正文上屏:开标签一律按字面量。连"疑似半个标签"也不再扣留——扣它是为了等标签,
            //这里不会再等,扣着只会让正文晚显示
            if (!_inThinking && !_tagAllowed)
            {
                _pending.Clear();
                Emit(buffer, onText, onThinking);
                return;
            }

            string tag = _inThinking ? CloseTag : OpenTag;
            int index = buffer.IndexOf(tag, StringComparison.Ordinal);
            if (index >= 0)
            {
                Emit(buffer[..index], onText, onThinking);
                //标签前面那段正文已经把"段首"用掉了:这个开标签是引用,不是标签
                if (!_inThinking && !_tagAllowed)
                {
                    _pending.Clear();
                    Emit(buffer[index..], onText, onThinking);
                    return;
                }

                _pending.Clear();
                _pending.Append(buffer[(index + tag.Length)..]);
                _inThinking = !_inThinking;
                continue;
            }

            // 未见完整标签:扣留可能是标签开头的末尾部分,其余立即分发
            int hold = PartialTagSuffixLength(buffer, tag);
            Emit(buffer[..^hold], onText, onThinking);
            _pending.Clear();
            _pending.Append(buffer[^hold..]);
            return;
        }
    }

    /// <summary>
    /// 流结束,清空扣留内容:仍在思考区段的余量归思考,否则(含疑似半个标签)按正文原样放出
    /// </summary>
    /// <param name="onText">正文回调</param>
    /// <param name="onThinking">思考回调</param>
    public void Complete(Action<string> onText, Action<string> onThinking)
    {
        Emit(_pending.ToString(), onText, onThinking);
        ResetSegment();
    }

    /// <summary>
    /// 标记本发已由框架给出结构化思考：后续正文里的 &lt;think&gt; 一律按字面量
    /// </summary>
    public void DisableInlineTags()
    {
        _inlineTagsEnabled = false;
        _inThinking = false; //禁用态下 Complete 也按正文放出，不把扣留的半标签误计成思考
    }

    /// <summary>
    /// 完整复位（新的一发、切换会话、整段重发）：连「本发有没有结构化思考」一起重新判
    /// </summary>
    public void Reset()
    {
        ResetSegment();
        _inlineTagsEnabled = true;
    }

    /// <summary>
    /// 只收掉当前段。段边界不能抹掉「本发已给出结构化思考」：抹掉之后回答里引用的
    /// &lt;think&gt; 又会被当成真标签把正文切进思考卡。本地内联模型从没调过
    /// <see cref="DisableInlineTags"/>，这里也不影响它跨段继续认标签。
    /// </summary>
    private void ResetSegment()
    {
        _pending.Clear();
        _inThinking = false;
        _tagAllowed = true; //下一段可以是新的一条消息:开标签重新算数
    }

    private void Emit(string segment, Action<string> onText, Action<string> onThinking)
    {
        if (segment.Length == 0) return;
        if (_inThinking)
        {
            onThinking(segment);
            return;
        }

        //只有空白不算"吐出正文":模板常在思考段前给几个换行/空格
        if (!IsWhiteSpace(segment)) _tagAllowed = false;
        onText(segment);
    }

    private static bool IsWhiteSpace(string segment)
    {
        foreach (char c in segment)
        {
            if (!char.IsWhiteSpace(c)) return false;
        }

        return true;
    }

    /// <summary>
    /// 末尾有多少个字符可能是 tag 的开头(不含完整 tag 的情况)
    /// </summary>
    private static int PartialTagSuffixLength(string buffer, string tag)
    {
        int max = Math.Min(tag.Length - 1, buffer.Length);
        for (int length = max; length > 0; length--)
        {
            if (string.CompareOrdinal(buffer, buffer.Length - length, tag, 0, length) == 0) return length;
        }

        return 0;
    }
}
