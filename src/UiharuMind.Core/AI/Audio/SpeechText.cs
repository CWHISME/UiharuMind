using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 把要朗读的文字整理成念得出来的样子：去掉 Markdown 记号、代码块、链接地址与表情，切成适合逐句合成的段，并粗判主要语种
/// </summary>
public static partial class SpeechText
{
    /// <summary>
    /// 一段最多多少字。太长合成首包慢、打断不灵；太短句间停顿碎
    /// </summary>
    public const int MaxChunkLength = 160;

    /// <summary>
    /// 第一段尽量短：先出声，后面的边念边合成
    /// </summary>
    public const int FirstChunkLength = 60;

    /// <summary>
    /// 去掉念不出来、念了也没意义的东西
    /// </summary>
    /// <param name="text">原文（通常是 Markdown）</param>
    /// <returns>纯文字，段落间以换行分隔；什么都不剩为空</returns>
    public static string Clean(string text)
    {
        string result = ThinkBlock().Replace(text, "");
        result = FencedCode().Replace(result, "\n");
        result = Image().Replace(result, "");
        result = Link().Replace(result, "$1");
        result = Url().Replace(result, "");
        result = HtmlTag().Replace(result, "");
        result = InlineCode().Replace(result, "$1");
        result = LinePrefix().Replace(result, "");
        result = TableRule().Replace(result, "");
        result = result.Replace('|', ' ');
        result = Emphasis().Replace(result, "");
        result = RemoveSymbols(result);
        result = Spaces().Replace(result, " ");
        result = BlankLines().Replace(result, "\n");
        return result.Trim();
    }

    /// <summary>
    /// 按句切段：句末标点与换行处断开，短句并进同一段，超长的句子在逗号处再断，仍不够就硬切
    /// </summary>
    /// <param name="text">已整理的文字</param>
    /// <returns>段</returns>
    public static IReadOnlyList<string> Split(string text)
    {
        List<string> chunks = [];
        StringBuilder current = new();
        foreach (string sentence in Sentences(text))
        {
            int limit = chunks.Count == 0 ? FirstChunkLength : MaxChunkLength;
            if (current.Length > 0 && current.Length + sentence.Length > limit)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            current.Append(sentence);
        }

        if (current.Length > 0) chunks.Add(current.ToString().Trim());
        return chunks.Where(x => x.Length > 0).ToList();
    }

    /// <summary>
    /// 粗判主要语种：汉字 → zh，假名 → ja，谚文 → ko，其余按拉丁字母算 en；没有文字为 null
    /// </summary>
    /// <param name="text">文字</param>
    /// <returns>语种代码</returns>
    public static string? DetectLanguage(string text)
    {
        int han = 0;
        int kana = 0;
        int hangul = 0;
        int latin = 0;
        foreach (char c in text)
        {
            if (c is >= '぀' and <= 'ヿ') kana++;
            else if (c is >= '가' and <= '힯') hangul++;
            else if (c is >= '一' and <= '鿿') han++;
            else if (char.IsAsciiLetter(c)) latin++;
        }

        // 日文里汉字多，有假名就算日文；中文按字算一个词，英文按字母算，所以汉字乘个系数再比
        if (kana > 0 && kana * 4 >= han) return "ja";
        if (hangul > 0 && hangul >= han) return "ko";
        if (han * 4 >= latin && han > 0) return "zh";
        return latin > 0 ? "en" : han > 0 ? "zh" : null;
    }

    // 句子带着自己的结束标点；长句在逗号处再拆，仍超长就硬切
    private static IEnumerable<string> Sentences(string text)
    {
        foreach (string sentence in SentenceEnd().Split(text).Where(x => x.Trim().Length > 0))
        {
            if (sentence.Length <= MaxChunkLength)
            {
                yield return sentence;
                continue;
            }

            foreach (string clause in Clause().Split(sentence).Where(x => x.Length > 0))
                for (int i = 0; i < clause.Length; i += MaxChunkLength)
                    yield return clause.Substring(i, Math.Min(MaxChunkLength, clause.Length - i));
        }
    }

    // 表情、图形符号与它们的变体选择符念出来是乱码或被跳过，直接去掉
    private static string RemoveSymbols(string text)
    {
        StringBuilder builder = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, i);
            bool isPair = char.IsSurrogatePair(text, i);
            if (category is not (UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate) &&
                text[i] is not ('️' or '‍'))
            {
                builder.Append(text[i]);
                if (isPair) builder.Append(text[i + 1]);
            }

            if (isPair) i++;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"(```|~~~)[\s\S]*?(\1|$)")]
    private static partial Regex FencedCode();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex InlineCode();

    // 行首的标题井号、引用号、列表记号
    [GeneratedRegex(@"^[ \t]*(#{1,6}[ \t]+|>+[ \t]?|[-*+][ \t]+|\d+[.)][ \t]+)", RegexOptions.Multiline)]
    private static partial Regex LinePrefix();

    [GeneratedRegex(@"^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(\|[ \t]*:?-{3,}:?[ \t]*)*\|?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex TableRule();

    [GeneratedRegex(@"(\*{1,3}|_{2,3}|~~)")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex BlankLines();

    // 中文句末标点、换行，以及后面跟空白的英文句号问号叹号；切点留在前一句末尾
    [GeneratedRegex(@"(?<=[。！？!?；;…\n])|(?<=[.](?=\s))")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?<=[，,、：:])")]
    private static partial Regex Clause();
}
