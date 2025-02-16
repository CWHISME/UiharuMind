/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using ColorCode.Styling;
using Markdig;
using Markdown.ColorCode;

namespace UiharuMind.Core.Core.Utils;

public static class MarkdownUtils
{
    private static readonly MarkdownPipeline PipelineLight = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseColorCode(HtmlFormatterType.Style, styleDictionary: StyleDictionary.DefaultLight,
            defaultLanguageId: "csharp")
        // 添加自定义处理器，第一是去掉段落的顶部和底部的空白
        // 第二是增加额外的自定义样式
        .Use<CustomParagraphExtension>()
        // .Use<CustomHeaderExtension>()
        // .Use<CustomFooterExtension>()
        .Build();

    private static readonly MarkdownPipeline PipelineDark = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseColorCode(HtmlFormatterType.Style, styleDictionary: StyleDictionary.DefaultDark,
            defaultLanguageId: "csharp")
        // 添加自定义处理器，第一是去掉段落的顶部和底部的空白
        // 第二是增加额外的自定义样式
        .Use<CustomParagraphExtension>()
        // .Use<CustomHeaderExtension>()
        // .Use<CustomFooterExtension>()
        .Build();

    // public static bool IsDarkTheme;

//     private const string HtmlHeadStyle = @"
//     <head>
//         <style>
//             /* 全局字体设置 */
//             * {{
//                 font-family: 'Dream Han Sans CN';
//                 font-size: 14px;
//             }}
//
//             /* 代码部分的字体设置 */
//             pre {{
//                 font-family: 'Dream Han Sans CN';
//             }}
//         </style>
//     </head>";
//
//     /// <summary>
//     /// 黑色主题
//     /// </summary>
//     public const string HtmlBodyDarkStyle = HtmlHeadStyle + "<body style='color: #fff;'>{0}</body>";
//
//     /// <summary>
//     /// 白色主题
//     /// </summary>
//     public const string HtmlBodyLightStyle = HtmlHeadStyle + "<body style='color: #000;'>{0}</body>";

    // public const string HtmlBodyStyleEnd = "</body>";

    // public const string GlobalStyleSheet = "GlobalStyleSheet";

    // public static string GetGlobalStylesheet(string src, bool isDarkTheme)
    // {
    //     if (src == GlobalStyleSheet)
    //     {
    //         if (isDarkTheme)
    //         {
    //             return @"* { font-family: 'Dream Han Sans CN'; font-size: 14px; }
    //                      pre { font-family: 'Dream Han Sans CN' }";
    //         }
    //
    //         return @"* { font-family: 'Dream Han Sans CN'; font-size: 14px; }
    //                 pre { font-family: 'JetBrains Mono' }";
    //     }
    //
    //     return null;
    // }

    public static string ToHtml(string markdown, bool darkTheme)
    {
        // IsDarkTheme = darkTheme;
        // return (markdown);
        // return GetThemeSpecificHtml(Markdig.Markdown.ToHtml(markdown, darkTheme ? PipelineDark : PipelineLight), darkTheme);
        if (markdown.StartsWith("<think>", StringComparison.Ordinal))
        {
            markdown = markdown.Replace("<think>", "<div class=\"think\">");
            if (markdown.Contains("</think>"))
                markdown = markdown.Replace("</think>", "</div>");
            else markdown += "</div>";
        }

        return GetThemeSpecificHtml(Markdig.Markdown.ToHtml(markdown, PipelineDark), darkTheme);

        // if (darkTheme)
        // {
        //     html = string.Format(HtmlBodyDarkStyle, html);
        // }
        // else
        // {
        //     html = string.Format(HtmlBodyLightStyle, html);
        // }
        //
        // return html;
    }


    public static string ToHtml(string markdown)
    {
        return Markdig.Markdown.ToHtml(markdown, PipelineDark);
    }

    private static string GetThemeSpecificHtml(string text, bool isDarkTheme)
    {
        if (isDarkTheme)
        {
            // p {{font-family: 'OPPO Sans';font-size: 14px}}
            // ol {{font-family: 'OPPO Sans';font-size: 14px;}}
            // pre {{ font-family: 'JetBrains Mono','OPPO Sans' }}
            // code {{ font-family: 'JetBrains Mono','OPPO Sans' }}


            //去掉第一个和最后一个 p 标签的上下边距，避免不整齐
            return @$"<head><style>
                         * {{font-family: 'HarmonyOS Sans';font-size: 14px;}}
                         li p {{ margin-top: 0px; margin-bottom: 0px; }}
                         pre {{ font-family: 'JetBrains Mono','HarmonyOS Sans'; }}
                         code {{ font-family: 'JetBrains Mono','HarmonyOS Sans'; }}
                        .think {{
                            background-color: #202020;
                            color: #888; /* 偏灰色 */
                            padding: 10px;
                            border-radius: 5px;
                            font-style: italic;
                        }}
                     </style></head>
                     <div style='color: #fff;'>
                         {text}
                     </div>";
        }

        return @$"<head><style>
                         * {{font-family: 'HarmonyOS Sans';font-size: 14px;}}
                         li p {{ margin-top: 0px; margin-bottom: 0px; }}
                         pre {{ font-family: 'HarmonyOS Sans'; }}
                         code {{ font-family: 'JetBrains Mono','HarmonyOS Sans'; }}
                        .think {{
                            background-color: #f0f0f0;
                            color: #666; /* 偏灰色 */
                            padding: 10px;
                            border-radius: 5px;
                            font-style: italic;
                        }}
                     </style></head>
                     <div style='color: #000;'>
                         {text}
                     </div>";
    }
}