using System.Text;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 流式 &lt;think&gt; 标签解析器:把本地/远程模型混在正文流里的思考段与正文分离。
/// 标签可能被流式增量任意切断(如 "&lt;thi" + "nk&gt;"),
/// 末尾疑似半个标签的部分会被扣留,待后续增量或 <see cref="Complete"/> 时定夺。
/// </summary>
public class ThinkTagStreamParser
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    private readonly StringBuilder _pending = new();
    private bool _inThinking;

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
            string tag = _inThinking ? CloseTag : OpenTag;
            int index = buffer.IndexOf(tag, StringComparison.Ordinal);
            if (index >= 0)
            {
                Emit(buffer[..index], onText, onThinking);
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
    }

    private void Emit(string segment, Action<string> onText, Action<string> onThinking)
    {
        if (segment.Length == 0) return;
        if (_inThinking) onThinking(segment);
        else onText(segment);
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
