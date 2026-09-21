using System.Collections.Generic;
using UiharuMind.Localization.Generator.Emit;
using UiharuMind.Localization.Generator.Model;

namespace UiharuMind.Localization.Generator.Tests;

public class LangKeyEmitterTests
{
    [Fact]
    public void Emit_ProducesEnumWithIdentityMembers()
    {
        var keys = new List<ResourceKeyValue>
        {
            new("Send", "Send"),
            new("TrayMenuAbout", "About UiharuMind"),
        };

        var source = LangKeyEmitter.Emit("UiharuMind.Generated", "LangKey", "/repo/Lang.resx", keys);

        Assert.Contains("namespace UiharuMind.Generated;", source);
        Assert.Contains("public enum LangKey", source);
        Assert.Contains("    Send,", source);
        Assert.Contains("    TrayMenuAbout,", source);
        Assert.DoesNotContain("public const", source);
    }

    [Fact]
    public void Emit_EmptyKeys_StillProducesEnumShell()
    {
        var source = LangKeyEmitter.Emit(
            "UiharuMind.Generated",
            "LangKey",
            "/repo/Lang.resx",
            new List<ResourceKeyValue>());

        Assert.Contains("public enum LangKey", source);
        Assert.Contains("namespace UiharuMind.Generated;", source);
    }

    [Fact]
    public void Emit_KeyWithUnderscore_IsEmittedAsIs()
    {
        var keys = new List<ResourceKeyValue> { new("ScreenCaptureDockWindow_BtnEdit", "Edit") };

        var source = LangKeyEmitter.Emit("UiharuMind.Generated", "LangKey", "/repo/Lang.resx", keys);

        Assert.Contains("    ScreenCaptureDockWindow_BtnEdit,", source);
    }

    [Fact]
    public void Emit_ValueWithNewlinesAndXmlSpecials_EscapesToSingleLineDocComment()
    {
        var keys = new List<ResourceKeyValue> { new("MultiLine", "Line1\nLine2\t& <tag>\r\n") };

        var source = LangKeyEmitter.Emit("UiharuMind.Generated", "LangKey", "/repo/Lang.resx", keys);

        // 注释行是单行、XML 已转义、末尾控制字符被剥掉
        Assert.Contains("    /// <summary>Line1 Line2 &amp; &lt;tag&gt;</summary>", source);
        // 原始换行不得进入注释体
        Assert.DoesNotContain("\nLine2", source);
        // 注释行必须紧挨在成员行上方
        Assert.Contains("    /// <summary>Line1 Line2 &amp; &lt;tag&gt;</summary>\n    MultiLine,", source);
    }

    [Fact]
    public void Emit_WhitespaceValue_OmitsDocComment()
    {
        var keys = new List<ResourceKeyValue> { new("NoCopy", "   \n\t ") };

        var source = LangKeyEmitter.Emit("UiharuMind.Generated", "LangKey", "/repo/Lang.resx", keys);

        Assert.Contains("    NoCopy,", source);
        Assert.DoesNotContain("NoCopy</summary>", source);
    }
}