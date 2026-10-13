using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>Read 结果里原文部分剥掉行号前缀后的样子</summary>
/// <param name="Text">剥掉前缀的全文：原文在前，截断附注、大纲等附注原样跟在后面</param>
/// <param name="FirstLine">原文首行在文件里的行号；没有带前缀的行时为 1</param>
/// <param name="ContentLines">原文占前几行；后面的是附注，不是文件内容</param>
public readonly record struct ReadContent(string Text, int FirstLine, int ContentLines);

/// <summary>
/// Read 正文的行号前缀 <c>82\t</c>（cat -n 的样子）：工具这头加，Edit 与界面那头认、剥。
/// 带行号是为了让模型引用、跳读时不必自己数行（实测整份读完的文件，模型报的行号能偏十几行）；
/// 用制表符而不是 <c>82:</c>，是不与 Grep 命中行 <c>行号:</c> 同形。旧会话里的 <c>82→</c> 照样认得。
/// </summary>
public static class ReadLineNumbers
{
    private const char Separator = '\t';

    private static readonly Regex Prefix = new(@"^ *(\d+)[\t→]", RegexOptions.Compiled);

    /// <summary>
    /// 某行行号的前缀 <c>9\t</c>（左对齐，不补空格）。预算与渲染都从这里取
    /// </summary>
    /// <param name="lineNo">行号</param>
    /// <returns>行号前缀</returns>
    public static string PrefixFor(int lineNo) => lineNo.ToString(CultureInfo.InvariantCulture) + Separator;

    /// <summary>
    /// 给一段原文逐行加前缀。行号左对齐：右对齐的补位空格要占 Read 的字节预算。
    /// 剥前缀的正则仍容许前导空格，旧会话里右对齐的结果照样剥得动
    /// </summary>
    /// <param name="lines">原文各行</param>
    /// <param name="firstLine">首行在文件里的行号</param>
    /// <returns>带前缀的正文</returns>
    public static string Format(IReadOnlyList<string> lines, int firstLine)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(PrefixFor(firstLine + i)).Append(lines[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 剥掉 Read 结果开头那段原文的前缀（界面显示用）。附注不带前缀，到第一条不带前缀的行为止
    /// </summary>
    /// <param name="resultText">Read 的结果原文</param>
    /// <returns>剥好的全文与原文的行号范围</returns>
    public static ReadContent Strip(string resultText)
    {
        string[] lines = resultText.Split('\n');
        int firstLine = 1;
        int count = 0;
        for (; count < lines.Length; count++)
        {
            Match match = Prefix.Match(lines[count]);
            if (!match.Success) break;
            if (count == 0) firstLine = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            lines[count] = lines[count][match.Length..];
        }

        return new ReadContent(string.Join('\n', lines), firstLine, count);
    }

    /// <summary>
    /// 模型把 Read 的前缀抄进了 Edit 参数时剥掉。<b>每一行</b>都带前缀、且行号逐行加一（Read 抄来的样子）才算；
    /// 调用方还须先按原样匹配、找不到才用剥过的，免得误伤真以「数字+制表符」开头的原文
    /// </summary>
    /// <param name="text">oldString 或 newString</param>
    /// <param name="stripped">剥掉前缀后的文本</param>
    /// <returns>是否剥过</returns>
    public static bool TryStripCopied(string text, out string stripped)
    {
        stripped = text;
        if (text.Length == 0) return false;

        string[] lines = text.Split('\n');
        int last = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length; //以换行收尾时末段是空串，不要求它带前缀
        if (last == 0) return false;

        int expected = -1;
        for (int i = 0; i < last; i++)
        {
            Match match = Prefix.Match(lines[i]);
            if (!match.Success) return false;
            int number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (expected >= 0 && number != expected) return false;
            expected = number + 1;
            lines[i] = lines[i][match.Length..];
        }

        stripped = string.Join('\n', lines);
        return true;
    }

    /// <summary>
    /// oldString 已确认是从 Read 抄来的之后，剥掉 newString 里带前缀的那些行：
    /// 模型常只给抄来的行留着前缀、新写的行不带，或者随手编个行号，所以不要求每行都带、也不要求连续
    /// </summary>
    /// <param name="text">newString</param>
    /// <returns>剥掉前缀后的文本</returns>
    public static string StripEachCopied(string text) =>
        string.Join('\n', text.Split('\n').Select(line => line[Prefix.Match(line).Length..]));
}
