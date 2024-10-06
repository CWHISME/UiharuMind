using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using UiharuMind.Core.Core.Utils;

public class CustomParagraphExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            htmlRenderer.ObjectRenderers.ReplaceOrAdd<ParagraphRenderer>(new CustomParagraphRenderer());
        }
    }
}

/// <summary>
/// 自定义处理器，用于段落的渲染，第一是去掉段落的顶部和底部的空白
/// 第二是增加额外的自定义样式
/// </summary>
public class CustomParagraphRenderer : ParagraphRenderer
{
    private HtmlAttributes _attributesMarginTop = new HtmlAttributes();
    private HtmlAttributes _attributesMarginBottom = new HtmlAttributes();
    private HtmlAttributes _attributesMargiAll = new HtmlAttributes();

    // private HtmlAttributes _attributesGlobalCustom = new HtmlAttributes();

    public CustomParagraphRenderer()
    {
        _attributesMarginTop.AddProperty("style", "margin-top: 0px;");
        _attributesMarginBottom.AddProperty("style", " margin-bottom: 0px");
        _attributesMargiAll.AddProperty("style", "margin-top: 0px; margin-bottom: 0px");
        // <link rel = "Stylesheet" href = "StyleSheet" / >
        // _attributesGlobalCustom.AddProperty("rel", "StyleSheet");
        // _attributesGlobalCustom.AddProperty("href", MarkdownUtils.GlobalStyleSheet);
    }

    protected override void Write(HtmlRenderer renderer, ParagraphBlock obj)
    {
        // if (renderer is { IsFirstInContainer: false, IsLastInContainer: false })
        if (obj.Parent?.Parent != null)
        {
            base.Write(renderer, obj);
            return;
        }

        // var parent = obj.Parent;
        // bool isFirst = renderer.IsFirstInContainer && parent is { Count: > 0 } && parent[0] == obj;
        // bool isLast = renderer.IsLastInContainer && parent != null && parent.LastChild == obj;

        bool isFirst = renderer.IsFirstInContainer;
        bool isLast = renderer.IsLastInContainer;
        // if (isFirst)
        // {
        //     renderer.Write("<link");
        //     renderer.WriteAttributes(_attributesGlobalCustom);
        //     renderer.Write("/>");
        //     //围上主体部分
        //     // renderer.Write("<div>");
        // }

        renderer.Write("<p");
        if (isFirst && isLast)
        {
            //增加额外的自定义样式
            // renderer.WriteLine(MarkdownUtils.IsDarkTheme
            //     ? MarkdownUtils.HtmlBodyDarkStyleStart
            //     : MarkdownUtils.HtmlBodyLightStyleStart);
            //去掉段落的顶部和底部的空白
            // renderer.WriteEscape("<p style=\"margin-top: 0px;\">");

            // HtmlAttributes attributes = new HtmlAttributes();
            // attributes.AddProperty("style", "margin-top: 0px");
            renderer.WriteAttributes(_attributesMargiAll);
        }
        else if (isFirst)
        {
            renderer.WriteAttributes(_attributesMarginTop);
        }
        else if (isLast)
        {
            renderer.WriteAttributes(_attributesMarginBottom);
        }

        // if (isLast)
        // {
        //     // HtmlAttributes attributes = new HtmlAttributes();
        //     attributes.AddProperty("style", "margin-bottom: 0px");
        //     // renderer.Write('>');
        //     // renderer.WriteEscape("<p style=\"margin-bottom: 0px;\">");
        // }

        // renderer.WriteAttributes(attributes);

        // else
        // {
        //     renderer.WriteEscape("<p>");
        // }
        renderer.Write('>');

        renderer.WriteLeafInline(obj);
        renderer.WriteLine("</p>");

        //结束额外的自定义样式
        // if (isLast)
        // {
        //     renderer.WriteLine(MarkdownUtils.HtmlBodyStyleEnd);
        // }
        //封闭主体部分
        // if (isLast) renderer.Write("</div>");
    }
}