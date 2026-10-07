using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// GitHub 页面正文的 UI 噪音清理:按钮文字、导航提示、重复信息与作者头像删掉,
/// issue 正文、评论、Activity 事件、正文截图与 Metadata 区块保留。样例取自真实抓取输出。
/// </summary>
public class GitHubPageCleanerTests
{
    [Theory]
    [InlineData("https://github.com/ggml-org/llama.cpp/issues/30007")]
    [InlineData("https://www.github.com/x/y")]
    public void CanClean_GitHubHosts_True(string url)
    {
        Assert.True(new GitHubPageCleaner().CanClean(url));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://raw.githubusercontent.com/x/y/main/readme.md")]
    [InlineData("not a url")]
    [InlineData("")]
    public void CanClean_OtherHosts_False(string url)
    {
        Assert.False(new GitHubPageCleaner().CanClean(url));
    }

    /// <summary>Firecrawl 路的输出:导航、登录横幅、按钮文字、标题重复段、头部标签、头像都要清掉</summary>
    [Fact]
    public void Clean_FirecrawlStyle_StripsUiNoise_KeepsContent()
    {
        const string text = """
            [Skip to content](https://github.com/ggml-org/llama.cpp/issues/30007#start-of-content)

            You signed in with another tab or window. [Reload](https://github.com/ggml-org/llama.cpp/issues/30007) to refresh your session.You signed out in another tab or window. [Reload](https://github.com/ggml-org/llama.cpp/issues/30007) to refresh your session.You switched accounts on another tab or window. [Reload](https://github.com/ggml-org/llama.cpp/issues/30007) to refresh your session.Dismiss alert

            {{ message }}

            # Eval bug: NemotronLabs-AI-for-Media-Sports-Tennis, loading issue\#30007

            [Eval bug: NemotronLabs-AI-for-Media-Sports-Tennis, loading issue](https://github.com/ggml-org/llama.cpp/issues/30007#top)#30007

            [New issue](https://github.com/login?return_to=https://github.com/ggml-org/llama.cpp/issues/30007)

            Copy link

            Open

            [bug-unconfirmed](https://github.com/ggml-org/llama.cpp/issues?q=state%3Aopen%20label%3A%22bug-unconfirmed%22)

            ## Description

            [![@FedericoFB](https://avatars.githubusercontent.com/u/337280318?v=4&size=48)](https://github.com/FedericoFB)

            [FedericoFB](https://github.com/FedericoFB)

            body text with [link](https://x.com)

            Issue body actions

            ### Name and Version

            - llama.cpp release b11368

            ## Activity

            [![](https://avatars.githubusercontent.com/u/337280318?s=64&v=4)FedericoFB](https://github.com/FedericoFB)

            added

            ### FedericoFB commented

            comment content here

            ![Image](https://private-user-images.githubusercontent.com/337280318/665936506.png)

            [Sign up for free](https://github.com/signup?return_to=https://github.com/ggml-org/llama.cpp/issues/30007)** to join this conversation on GitHub.** Already have an account? [Sign in to comment](https://github.com/login?return_to=https://github.com/ggml-org/llama.cpp/issues/30007)

            ## Metadata

            ## Metadata

            ### Labels

            [bug-unconfirmed](https://github.com/ggml-org/llama.cpp/issues?q=state%3Aopen%20label%3A%22bug-unconfirmed%22)

            ## Issue actions

            - ![](https://github.githubassets.com/assets/github-copilot-app-light-15ad5534265eeacd.svg)Open in GitHub Copilot app

            You can’t perform that action at this time.
            """;

        string cleaned = new GitHubPageCleaner().Clean(text);

        // 删掉的 UI 噪音与重复信息
        Assert.DoesNotContain("Skip to content", cleaned);
        Assert.DoesNotContain("You signed in", cleaned);
        Assert.DoesNotContain("{{ message }}", cleaned);
        Assert.DoesNotContain("New issue", cleaned);
        Assert.DoesNotContain("Copy link", cleaned);
        Assert.DoesNotContain("Open\n\n", cleaned);
        Assert.DoesNotContain("Issue body actions", cleaned);
        Assert.DoesNotContain("Sign up for free", cleaned);
        Assert.DoesNotContain("Issue actions", cleaned);
        Assert.DoesNotContain("Open in GitHub Copilot app", cleaned);
        Assert.DoesNotContain("You can’t perform that action at this time.", cleaned);
        Assert.DoesNotContain("[Eval bug: NemotronLabs", cleaned); // 标题重复链接段
        Assert.DoesNotContain("[![", cleaned); // 作者头像(正文区与事件流两处)
        // 头部标签删掉后,只剩 Metadata 区块里那一份
        Assert.Single(Regex.Matches(cleaned, Regex.Escape("[bug-unconfirmed]")));
        Assert.Single(Regex.Matches(cleaned, "## Metadata")); // 重复渲染只留一份

        // 保留的正文内容
        Assert.Contains("# Eval bug: NemotronLabs-AI-for-Media-Sports-Tennis", cleaned);
        Assert.Contains("## Description", cleaned);
        Assert.Contains("[FedericoFB](https://github.com/FedericoFB)", cleaned);
        Assert.Contains("body text with [link](https://x.com)", cleaned);
        Assert.Contains("### Name and Version", cleaned);
        Assert.Contains("- llama.cpp release b11368", cleaned);
        Assert.Contains("added", cleaned);
        Assert.Contains("### FedericoFB commented", cleaned);
        Assert.Contains("comment content here", cleaned);
        Assert.Contains("![Image](https://private-user-images.githubusercontent.com/337280318/665936506.png)", cleaned); // 正文截图
        Assert.Contains("### Labels", cleaned);
    }

    /// <summary>Direct 路的输出:按钮是裸文本,规则同样覆盖;重复的标题链接与区块标题也清掉</summary>
    [Fact]
    public void Clean_DirectStyle_StripsButtons_KeepsMetadata()
    {
        const string text = """
            # Eval bug: NemotronLabs-AI-for-Media-Sports-Tennis, loading issue #30007

            [Eval bug: NemotronLabs-AI-for-Media-Sports-Tennis, loading issue](/ggml-org/llama.cpp/issues/30007#top)#30007

            New issue

            Copy link

            Open

            Labels

            [bug-unconfirmed](https://github.com/ggml-org/llama.cpp/issues?q=state%3Aopen%20label%3A%22bug-unconfirmed%22)

            ## Description

            [FedericoFB](https://github.com/FedericoFB)

            opened [on Oct 5, 2026](https://github.com/ggml-org/llama.cpp/issues/30007#issue-5716238470)

            ### Operating systems

            Windows

            Reactions are currently unavailable

            ## Metadata

            ## Metadata

            ### Assignees

            No one assigned
            """;

        string cleaned = new GitHubPageCleaner().Clean(text);

        Assert.DoesNotContain("New issue", cleaned);
        Assert.DoesNotContain("Copy link", cleaned);
        Assert.DoesNotContain("Open\n\n", cleaned);
        Assert.DoesNotContain("Labels\n\n", cleaned);
        Assert.DoesNotContain("Reactions are currently unavailable", cleaned);
        Assert.DoesNotContain("[Eval bug: NemotronLabs", cleaned);
        Assert.DoesNotContain("[bug-unconfirmed]", cleaned); // 头部标签删掉,Direct 样例里没有 Metadata 版本
        Assert.Single(Regex.Matches(cleaned, "## Metadata"));

        Assert.Contains("# Eval bug: NemotronLabs-AI-for-Media-Sports-Tennis", cleaned);
        Assert.Contains("## Description", cleaned);
        Assert.Contains("opened [on Oct 5, 2026]", cleaned);
        Assert.Contains("### Operating systems", cleaned);
        Assert.Contains("Windows", cleaned);
        Assert.Contains("### Assignees", cleaned);
        Assert.Contains("No one assigned", cleaned);
    }

    /// <summary>正文里的截图是内容必须保留;只删外层包链接的作者头像</summary>
    [Fact]
    public void Clean_KeepsInlineImages_StripsAvatarLinks()
    {
        const string text = """
            # Title

            ## Description

            body with screenshot:

            ![Image](https://private-user-images.githubusercontent.com/123/abc.png)

            [FedericoFB](https://github.com/FedericoFB)

            [![@FedericoFB](https://avatars.githubusercontent.com/u/1?v=4&size=48)](https://github.com/FedericoFB)

            [![](https://avatars.githubusercontent.com/u/1?s=64&v=4)FedericoFB](https://github.com/FedericoFB)
            """;

        string cleaned = new GitHubPageCleaner().Clean(text);

        Assert.Contains("![Image](https://private-user-images.githubusercontent.com/123/abc.png)", cleaned);
        Assert.DoesNotContain("[![", cleaned);
        Assert.DoesNotContain("avatars.githubusercontent.com", cleaned);
    }

    /// <summary>discussions 页:状态、计数、模板残留、编辑器工具栏都要清,问题与回复正文保留</summary>
    [Fact]
    public void Clean_DiscussionsStyle_StripsUiNoise_KeepsContent()
    {
        const string text = """
            # speculative decoding work with quantized model.  \#30057

            Unanswered

            [Kas1o](https://github.com/Kas1o)

            asked this question in

            [Q&A](https://github.com/ggml-org/llama.cpp/discussions/categories/q-a)

            11 hours agoOct 6, 2026·
            1 comment

            [Return to top](https://github.com/ggml-org/llama.cpp/discussions/30057#top)

            Discussion options

            ### Uh oh!

            There was an error while loading. [Please reload this page](https://github.com/ggml-org/llama.cpp/discussions/30057).

            # {{title}}

            Quote reply

            ## [![](https://avatars.githubusercontent.com/u/88198563?s=64&v=4)\ Kas1o](https://github.com/Kas1o) [11 hours agoOct 6, 2026](https://github.com/ggml-org/llama.cpp/discussions/30057\#discussion-10963442)

            |     |

            | --- |

            | The Drafter model is trained on unquantized models. |

            1You must be logged in to vote

            All reactions

            ## Replies:   1 comment

            Comment options

            ### Uh oh!

            There was an error while loading. [Please reload this page](https://github.com/ggml-org/llama.cpp/discussions/30057).

            # {{title}}

            Quote reply

            ### [![](https://avatars.githubusercontent.com/u/70004933?s=64&v=4)\ dev-jinwoohong](https://github.com/dev-jinwoohong) [3 hours agoOct 7, 2026](https://github.com/ggml-org/llama.cpp/discussions/30057\#discussioncomment-18787099)

            |     |

            | --- |

            | Hi [@Kas1o](https://github.com/Kas1o), Yes, a drafter trained against a full-precision target can work. |

            0 replies

            Category

            [Q&A](https://github.com/ggml-org/llama.cpp/discussions/categories/q-a)

            None yet

            2 participants

            Heading

            Bold

            Italic

            Quote

            Code

            Link

            * * *

            Numbered list

            Unordered list

            Task list

            * * *

            Attach files

            Mention

            Reference

            # Select a reply

            Loading

            [Create a new saved reply](https://github.com/ggml-org/llama.cpp/discussions/30057)

            🙏1 reacted with thumbs up emoji
            """;

        string cleaned = new GitHubPageCleaner().Clean(text);

        // 删掉的 UI 噪音与模板残留
        Assert.DoesNotContain("Unanswered", cleaned);
        Assert.DoesNotContain("asked this question in", cleaned);
        Assert.DoesNotContain("1 comment", cleaned);
        Assert.DoesNotContain("Return to top", cleaned);
        Assert.DoesNotContain("Discussion options", cleaned);
        Assert.DoesNotContain("Uh oh", cleaned);
        Assert.DoesNotContain("There was an error", cleaned);
        Assert.DoesNotContain("{{title}}", cleaned);
        Assert.DoesNotContain("Quote reply", cleaned);
        Assert.DoesNotContain("logged in to vote", cleaned);
        Assert.DoesNotContain("All reactions", cleaned);
        Assert.DoesNotContain("0 replies", cleaned);
        Assert.DoesNotContain("Category", cleaned);
        Assert.DoesNotContain("None yet", cleaned);
        Assert.DoesNotContain("participants", cleaned);
        Assert.DoesNotContain("Heading", cleaned);
        Assert.DoesNotContain("* * *", cleaned);
        Assert.DoesNotContain("Select a reply", cleaned);
        Assert.DoesNotContain("Loading", cleaned);
        Assert.DoesNotContain("Create a new saved reply", cleaned);
        Assert.DoesNotContain("reacted with", cleaned);

        // 保留的内容:标题、作者、问题正文、回复正文
        Assert.Contains("# speculative decoding work with quantized model", cleaned);
        Assert.Contains("[Kas1o](https://github.com/Kas1o)", cleaned);
        Assert.Contains("The Drafter model is trained on unquantized models", cleaned);
        Assert.Contains("dev-jinwoohong](https://github.com/dev-jinwoohong)", cleaned); // 作者行是 [![](avatar)\ dev-jinwoohong](url)
        Assert.Contains("Hi [@Kas1o](https://github.com/Kas1o), Yes, a drafter", cleaned);
        Assert.Contains("11 hours agoOct 6, 2026", cleaned);
    }

    /// <summary>
    /// Firecrawl 常把计数/按钮词并进相邻段(如 "11 hours ago...·\n1 comment"),
    /// 段落级精确匹配够不着,行级过滤要能把独立噪音行剔掉。
    /// </summary>
    [Fact]
    public void Clean_StripsNoiseLinesMergedIntoSameParagraph()
    {
        const string text = """
            11 hours agoOct 6, 2026·
            1 comment

            ## Replies:   1 comment

            body line
            Author
            more text
            """;

        string cleaned = new GitHubPageCleaner().Clean(text);

        Assert.Contains("11 hours agoOct 6, 2026", cleaned);
        Assert.DoesNotContain("1 comment", cleaned);
        Assert.DoesNotContain("Author", cleaned);
        Assert.Contains("body line", cleaned);
        Assert.Contains("more text", cleaned);
    }

    /// <summary>仓库主页:分支/标签/星标计数、导航按钮、空区块标题清掉,commit、文件列表表格与 README 保留</summary>
    [Fact]
    public void Clean_RepoHomeStyle_StripsUiNoise_KeepsReadme()
    {
        const string text = """
            main

            [**1** Branch](https://github.com/CWHISME/UiharuMind/branches) [**7** Tags](https://github.com/CWHISME/UiharuMind/tags)

            [Go to Branches page](https://github.com/CWHISME/UiharuMind/branches)[Go to Tags page](https://github.com/CWHISME/UiharuMind/tags)

            Go to file

            Open more actions menu

            ## Latest commit

            [引擎检查更新被限流走 feed 兜底时也带上发布时间与说明](https://github.com/CWHISME/UiharuMind/commit/3742218)

            success

            14 hours agoOct 6, 2026

            [3742218](https://github.com/CWHISME/UiharuMind/commit/3742218)

            ## History

            [862 Commits](https://github.com/CWHISME/UiharuMind/commits/main/)

            Open commit details

            ## Folders and files

            | Name | Last commit message | Last commit date |
            | --- | --- | --- |
            | [docs](https://github.com/CWHISME/UiharuMind/tree/main/docs) | [本地模型设置按 Jan 改为分节设置页](https://github.com/CWHISME/UiharuMind/commit/ca0d95e) | 14 hours agoOct 6, 2026 |

            View all files

            ## Repository files navigation

            # UiharuMind

            [Permalink: UiharuMind](https://github.com/CWHISME/UiharuMind#uiharumind)

            UiharuMind 目前支持的功能有：

            1. 本地化部署 AI 模型

            ## About

            UiharuMind 是一个开源的 AI 大模型工具。

            ### Resources

            [Readme](https://github.com/CWHISME/UiharuMind#readme-ov-file)

            ### Stars

            **8** stars

            ### Watchers

            **2** watching

            ### Forks

            [**2** forks](https://github.com/CWHISME/UiharuMind/forks)

            [Report repository](https://github.com/contact/report-content?content_url=UiharuMind)

            ## Releases

            ## Packages

            ## Used by

            ## Contributors

            ## Languages
            """;

        string cleaned = new GitHubPageCleaner().Clean(text);

        // 删掉的仓库主页 UI
        Assert.DoesNotContain("main\n", cleaned); // 分支名独立段删掉;表格 URL 里的 /tree/main/docs 是内容
        Assert.DoesNotContain("Branch](https://github.com/CWHISME/UiharuMind/branches)", cleaned);
        Assert.DoesNotContain("Go to Branches page", cleaned);
        Assert.DoesNotContain("Go to file", cleaned);
        Assert.DoesNotContain("Open more actions menu", cleaned);
        Assert.DoesNotContain("success", cleaned);
        Assert.DoesNotContain("## History", cleaned);
        Assert.DoesNotContain("862 Commits", cleaned);
        Assert.DoesNotContain("Open commit details", cleaned);
        Assert.DoesNotContain("View all files", cleaned);
        Assert.DoesNotContain("Repository files navigation", cleaned);
        Assert.DoesNotContain("### Resources", cleaned);
        Assert.DoesNotContain("### Stars", cleaned);
        Assert.DoesNotContain("### Watchers", cleaned);
        Assert.DoesNotContain("### Forks", cleaned);
        Assert.DoesNotContain("8** stars", cleaned);
        Assert.DoesNotContain("forks](https://github.com/CWHISME/UiharuMind/forks)", cleaned);
        Assert.DoesNotContain("Report repository", cleaned);
        Assert.DoesNotContain("## Releases", cleaned);
        Assert.DoesNotContain("## Packages", cleaned);
        Assert.DoesNotContain("## Used by", cleaned);
        Assert.DoesNotContain("## Contributors", cleaned);
        Assert.DoesNotContain("## Languages", cleaned);

        // 保留的核心内容
        Assert.Contains("## Latest commit", cleaned);
        Assert.Contains("[引擎检查更新被限流走 feed 兜底时也带上发布时间与说明](https://github.com/CWHISME/UiharuMind/commit/3742218)", cleaned);
        Assert.Contains("14 hours agoOct 6, 2026", cleaned);
        Assert.Contains("## Folders and files", cleaned);
        Assert.Contains("| [docs](https://github.com/CWHISME/UiharuMind/tree/main/docs)", cleaned);
        Assert.Contains("# UiharuMind", cleaned);
        Assert.Contains("UiharuMind 目前支持的功能有：", cleaned);
        Assert.Contains("## About", cleaned);
        Assert.Contains("UiharuMind 是一个开源的 AI 大模型工具。", cleaned);
    }

    /// <summary>非 GitHub 站点不套清理,正文原样返回</summary>
    [Fact]
    public void Clean_NonGithubHost_IsReturnedUnchanged()
    {
        const string text = "New issue\n\nCopy link\n\n正文段落";
        Assert.Equal(text, SitePageCleaners.Clean("https://example.com/x", text));
    }
}
