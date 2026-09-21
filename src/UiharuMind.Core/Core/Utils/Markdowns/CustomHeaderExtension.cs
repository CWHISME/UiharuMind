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

// using Markdig;
// using Markdig.Renderers;
// using Markdig.Renderers.Html;
// using Markdig.Syntax;
// using UiharuMind.Core.Core.Utils;
//
// /// <summary>
// /// 自定义页眉，全局 style 链接，以及主体包围部分
// /// </summary>
// public class CustomHeaderExtension : IMarkdownExtension
// {
//     public void Setup(MarkdownPipelineBuilder pipeline)
//     {
//     }
//
//     public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
//     {
//         if (renderer is HtmlRenderer htmlRenderer)
//         {
//             htmlRenderer.ObjectRenderers.Add(new HeaderRenderer());
//         }
//     }
// }
//
// public class HeaderRenderer : HtmlObjectRenderer<MarkdownDocument>
// {
// //     private const string HtmlHeadStyle = @"
// //     <head>
// //         <style>
// //             /* 全局字体设置 */
// //             * {{
// //                 font-family: 'Dream Han Sans CN';
// //                 font-size: 14px;
// //             }}
// //
// //             /* 代码部分的字体设置 */
// //             pre {{
// //                 font-family: 'Dream Han Sans CN';
// //             }}
// //         </style>
// //     </head>";
//
// // if (isDarkTheme)
// // {
// //     return @"div { color: #fff }
// //                          * { font-family: 'Dream Han Sans CN'; font-size: 14px; }
// //                          pre { font-family: 'Dream Han Sans CN' }";
// // }
// //
// // return @"div { color: #000 }
// //                     * { font-family: 'Dream Han Sans CN'; font-size: 14px; }
// //                     pre { font-family: 'JetBrains Mono' }";
//
//     private HtmlAttributes _attributesGlobalCustom = new HtmlAttributes();
//
//     private HtmlAttributes _attributesThemeDarkCustom = new HtmlAttributes();
//     private HtmlAttributes _attributesThemeLightCustom = new HtmlAttributes();
//
//     public HeaderRenderer()
//     {
//         _attributesGlobalCustom.AddProperty("rel", "StyleSheet");
//         _attributesGlobalCustom.AddProperty("href", MarkdownUtils.GlobalStyleSheet);
//
//         //主题定义
//         _attributesThemeDarkCustom.AddProperty("color", "#fff");
//         _attributesThemeLightCustom.AddProperty("color", "#000");
//     }
//
//     protected override void Write(HtmlRenderer renderer, MarkdownDocument obj)
//     {
//         // 写入自定义的页眉内容
//         // 在渲染开始时插入内容
//         // renderer.Write("<link");
//         // renderer.WriteAttributes(_attributesGlobalCustom);
//         // renderer.Write("/>");
//         renderer.Write("<Head>");
//         renderer.Write("<style>");
//         renderer.WriteAttributes(_attributesGlobalCustom);
//         renderer.Write("</style>");
//         renderer.Write("</Head>");
//         //围上主体部分
//         renderer.Write("<div");
//         renderer.WriteAttributes(MarkdownUtils.IsDarkTheme ? _attributesThemeDarkCustom : _attributesThemeLightCustom);
//         renderer.Write(">");
//
//         // 然后写入原始内容
//         renderer.WriteChildren(obj);
//     }
// }