// using Markdig;
// using Markdown.ColorCode;
// using Markdown.ColorCode.CSharpToColoredHtml;
//
// namespace UiharuMind.Core.Core.Utils;
//
// public static class MarkdownUtils
// {
//     private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
//         .UseAdvancedExtensions()
//         .UseColorCodeWithCSharpToColoredHtml(HtmlFormatterType.Style)
//         .Build();
//
//     public static string ToHtml(string markdown)
//     {
//         return Markdig.Markdown.ToHtml(markdown, _pipeline);
//     }
// }