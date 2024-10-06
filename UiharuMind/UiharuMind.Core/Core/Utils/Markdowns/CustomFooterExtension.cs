// using Markdig;
// using Markdig.Renderers;
// using Markdig.Renderers.Html;
// using Markdig.Syntax;
//
// /// <summary>
// /// 给 Markdown 文档添加自定义页脚，其实就是的对应包裹体
// /// 与 CustomHeaderExtension 对应
// /// </summary>
// public class CustomFooterExtension : IMarkdownExtension
// {
//     
//     public void Setup(MarkdownPipelineBuilder pipeline)
//     {
//     }
//
//     public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
//     {
//         if (renderer is HtmlRenderer htmlRenderer)
//         {
//             htmlRenderer.ObjectRenderers.Add(new FooterRenderer());
//         }
//     }
// }
//
// public class FooterRenderer : HtmlObjectRenderer<MarkdownDocument>
// {
//     protected override void Write(HtmlRenderer renderer, MarkdownDocument obj)
//     {
//         // 先写入原始内容
//         renderer.WriteChildren(obj);
//
//         //封闭主体部分
//         renderer.Write("/div");
//     }
// }