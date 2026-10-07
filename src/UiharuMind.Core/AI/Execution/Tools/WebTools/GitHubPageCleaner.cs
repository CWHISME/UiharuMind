using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// GitHub 页面正文的 UI 噪音清理:按钮文字、导航提示与重复信息
/// (Firecrawl / Direct 两路都带)从段落里剔掉,issue 正文、评论、
/// Activity 事件与 Metadata 区块保留。噪音名单是 GitHub 的界面文案,
/// 站点改版时要跟进维护。
/// </summary>
internal sealed partial class GitHubPageCleaner : ISitePageCleaner
{
    public bool CanClean(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Host is "github.com" or "www.github.com";
    }

    public string Clean(string text)
    {
        string[] paragraphs = text.Split("\n\n", StringSplitOptions.None);
        string? titleBody = ExtractTitleBody(paragraphs);
        bool pastDescription = false;
        string? lastKept = null;

        List<string> kept = [];
        foreach (string paragraph in paragraphs)
        {
            string t = paragraph.Trim();
            if (t.Length == 0) continue;

            if (t.StartsWith("## Description", StringComparison.Ordinal))
                pastDescription = true;

            if (t == "## Metadata" && lastKept == "## Metadata") continue; // 重复渲染的区块标题
            if (IsNoise(t)) continue;
            if (!pastDescription && IsHeaderNoise(t, titleBody)) continue; // 正文前的重复信息

            // 段落内行级噪音:独立词被 Firecrawl 并进相邻段时(如 "11 hours ago...·\n1 comment")
            // 段落级精确匹配够不着,拆行剔掉噪音行,剔空则整段不保留。
            string stripped = StripNoiseLines(t);
            if (stripped.Length == 0) continue;

            kept.Add(stripped);
            lastKept = stripped;
        }

        return string.Join("\n\n", kept);
    }

    /// <summary>去掉段落内精确等于噪音词的独立行;单段落(无换行)原样返回</summary>
    private static string StripNoiseLines(string paragraph)
    {
        string[] lines = paragraph.Split('\n');
        if (lines.Length == 1) return paragraph;

        return string.Join("\n", lines.Where(l => !IsNoiseLine(l.Trim())));
    }

    /// <summary>行级噪音:Firecrawl 常把计数/按钮词并进时间行等相邻段,独立成段时已被 IsNoise 处理</summary>
    private static bool IsNoiseLine(string line)
    {
        return line is "1 comment" or "0 replies" or "2 participants" or "Copy link" or "Open"
            or "New issue" or "Labels" or "Author" or "More actions" or "Loading"
            or "None yet" or "Category" or "All reactions" or "Unanswered"
            or "Discussion options" or "Quote reply" or "Comment options"
            or "1You must be logged in to vote" or "Dismiss alert" or "{{ message }}";
    }

    /// <summary>主标题去掉 "# " 前缀与尾部 issue 编号(Firecrawl 转义成 \#30007,Direct 是 #30007)</summary>
    private static string? ExtractTitleBody(IEnumerable<string> paragraphs)
    {
        foreach (string paragraph in paragraphs)
        {
            string t = paragraph.Trim();
            if (t.StartsWith("# ", StringComparison.Ordinal))
                return TitleNumberSuffixRegex().Replace(t[2..].Trim(), "").Trim();
        }

        return null;
    }

    /// <summary>正文区开始前的重复信息:与主标题重复的链接段、头部标签链接(Metadata 里有完整版)</summary>
    private static bool IsHeaderNoise(string paragraph, string? titleBody)
    {
        if (titleBody is { Length: > 0 } &&
            paragraph.StartsWith($"[{titleBody}]", StringComparison.Ordinal))
            return true;

        return paragraph.StartsWith("[", StringComparison.Ordinal)
            && paragraph.Contains("issues?q=", StringComparison.Ordinal)
            && paragraph.Contains("label%3A", StringComparison.Ordinal);
    }

    // 段落级判定:精确匹配的按钮文字,或特征明确的前缀。
    // 只删"整段就是 UI 动作"的,绝不按单词删——issue/discussion 正文里出现同样的词不受影响。
    private static bool IsNoise(string paragraph)
    {
        return paragraph switch
        {
            // issue 页:按钮文字与导航提示
            "New issue" or "Copy link" or "Open" or "Labels" or "Issue body actions"
                or "Reactions are currently unavailable" or "You can’t perform that action at this time."
                or "{{ message }}" or "Dismiss alert" or "Author" or "More actions"
            // discussions 页:状态、计数、按钮与编辑器工具栏
            or "Unanswered" or "asked this question in" or "1 comment" or "Discussion options"
                or "### Uh oh!" or "Quote reply" or "Comment options"
                or "1You must be logged in to vote" or "All reactions" or "0 replies"
                or "Category" or "None yet" or "2 participants" or "Loading"
                or "Heading" or "Bold" or "Italic" or "Quote" or "Code" or "Link" or "* * *"
                or "Numbered list" or "Unordered list" or "Task list" or "Attach files"
                or "Mention" or "Reference"
            // 仓库主页:分支/标签计数、导航按钮、空区块标题
            or "main" or "success" or "Go to file" or "Open more actions menu"
                or "Open commit details" or "View all files"
                or "## History" or "## Repository files navigation"
                or "### Resources" or "### Stars" or "### Watchers" or "### Forks"
                or "## Releases" or "## Packages" or "## Used by" or "## Contributors"
                or "## Languages" => true,
            _ => paragraph.StartsWith("[Skip to content]", StringComparison.Ordinal)
                || paragraph.StartsWith("You signed in", StringComparison.Ordinal)
                || paragraph.StartsWith("You signed out", StringComparison.Ordinal)
                || paragraph.StartsWith("You switched accounts", StringComparison.Ordinal)
                || paragraph.StartsWith("[New issue]", StringComparison.Ordinal)
                || paragraph.StartsWith("[Sign up for free]", StringComparison.Ordinal)
                || paragraph.StartsWith("## Issue actions", StringComparison.Ordinal)
                || paragraph.StartsWith("[Return to top]", StringComparison.Ordinal)
                || paragraph.StartsWith("[Create a new saved reply]", StringComparison.Ordinal)
                || paragraph.StartsWith("# {{title}}", StringComparison.Ordinal)
                || paragraph.StartsWith("## Replies:", StringComparison.Ordinal)
                || paragraph.StartsWith("# Select a reply", StringComparison.Ordinal)
                || paragraph.StartsWith("There was an error while loading.", StringComparison.Ordinal)
                || (paragraph.StartsWith("- ", StringComparison.Ordinal)
                    && paragraph.Contains("Open in GitHub Copilot app", StringComparison.Ordinal))
                // 作者头像链接:外层链接包着图片 [![...](avatar)](profile)。
                // 注意不是 "![Image](...)"——那是正文里的截图,必须保留。
                || paragraph.StartsWith("[![", StringComparison.Ordinal)
                // 仓库主页:分支/标签/星标计数、导航与空区块相关段
                || (paragraph.StartsWith("[**", StringComparison.Ordinal)
                    && (paragraph.Contains(" Branch](", StringComparison.Ordinal)
                        || paragraph.Contains(" Tags](", StringComparison.Ordinal)
                        || paragraph.Contains(" forks](", StringComparison.Ordinal)))
                || paragraph.StartsWith("[Go to Branches page]", StringComparison.Ordinal)
                || paragraph.StartsWith("[View commit history for this file.]", StringComparison.Ordinal)
                || (paragraph.StartsWith("[", StringComparison.Ordinal)
                    && paragraph.Contains(" Commits](", StringComparison.Ordinal))
                || paragraph.StartsWith("[Report repository]", StringComparison.Ordinal)
                || (paragraph.StartsWith("**", StringComparison.Ordinal)
                    && paragraph.Length < 40
                    && (paragraph.EndsWith(" stars", StringComparison.Ordinal)
                        || paragraph.EndsWith(" watching", StringComparison.Ordinal)))
                // discussions 页:分类链接与表情反应段
                || paragraph.Contains("discussions/categories/", StringComparison.Ordinal)
                || (paragraph.Contains("reacted with", StringComparison.Ordinal)
                    && paragraph.Contains("emoji", StringComparison.Ordinal))
        };
    }

    [GeneratedRegex(@"\\?#\d+$")]
    private static partial Regex TitleNumberSuffixRegex();
}
