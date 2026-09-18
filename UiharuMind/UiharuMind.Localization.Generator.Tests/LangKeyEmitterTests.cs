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
}