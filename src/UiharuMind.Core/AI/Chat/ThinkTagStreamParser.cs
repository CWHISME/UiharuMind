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
/// <para>段边界(一次服务调用结束、工具调用、审批)之后是下一条消息的开头,由
/// <see cref="ThinkingBoundary.Dispatch"/> 收口时调 <see cref="Reset"/> 重新打开。</para>
/// </summary>
public class ThinkTagStreamParser
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    private readonly StringBuilder _pending = new();
    private bool _inThinking;
    private bool _tagAllowed = true; //本段还没吐出过可见正文:此时开标签才算标签

    /// <summary>
    /// 喂入一段流式增量,按当前所在区段把可确定的部分分发给正文或思考回调
    /// </summary>
    /// <param name="delta">增量文本</param>
    /// <param name="onText">正文回调</param>
    /// <param name="onThinking">思考回调</param>
    public void Feed(string delta, Action<string> onText, Action<string> onThinking)
    {
        if (string.IsNullOrEmpty(delta)) return;
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
        Reset();
    }

    /// <summary>
    /// 复位解析状态
    /// </summary>
    public void Reset()
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
