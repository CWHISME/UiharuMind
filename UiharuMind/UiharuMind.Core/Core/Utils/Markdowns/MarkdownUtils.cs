using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdown.ColorCode;
using Markdown.ColorCode.CSharpToColoredHtml;

namespace UiharuMind.Core.Core.Utils;

public static class MarkdownUtils
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseColorCodeWithCSharpToColoredHtml(HtmlFormatterType.Style)
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
        return GetThemeSpecificHtml(Markdig.Markdown.ToHtml(markdown, Pipeline), darkTheme);
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
        return Markdig.Markdown.ToHtml(markdown, Pipeline);
    }

    private static string GetThemeSpecificHtml(string text, bool isDarkTheme)
    {
        if (isDarkTheme)
        {
            //去掉第一个和最后一个 p 标签的上下边距，避免不整齐
            return @$"<head><style>
                         * {{font-family: 'Dream Han Sans CN';font-size: 14px;}}
                         pre {{ font-family: 'JetBrains Mono','Dream Han Sans CN' }}
                     </style></head>
                     <div style='color: #fff;'>
                         {text}
                     </div>";
        }

        return @$"<head><style>
                         * {{font-family: 'Dream Han Sans CN';font-size: 14px}}
                         pre {{ font-family: 'JetBrains Mono','Dream Han Sans CN' }}
                     </style></head>
                     <div style='color: #000;'>
                         {text}
                     </div>";
    }
}