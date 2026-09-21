using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 钉选名到条目种类的纯映射。选中回填与缺失保留都走它——
/// 缺失时保留名字回落全局的行为钉死在这里，不依赖任何单例。
/// </summary>
public class SessionModelSelectionTests
{
    [Fact]
    public void NoOverride_SelectsDefault()
    {
        Assert.Equal(SessionModelSelectionKind.Default,
            SessionModelOption.ResolveSelection(null, ["m1"]));
        Assert.Equal(SessionModelSelectionKind.Default,
            SessionModelOption.ResolveSelection("", ["m1"]));
    }

    [Fact]
    public void PinnedInList_SelectsModel()
    {
        Assert.Equal(SessionModelSelectionKind.Model,
            SessionModelOption.ResolveSelection("m1", ["m1", "m2"]));
    }

    [Fact]
    public void PinnedNotInList_SelectsMissing()
    {
        Assert.Equal(SessionModelSelectionKind.Missing,
            SessionModelOption.ResolveSelection("m9", ["m1", "m2"]));
    }

    [Fact]
    public void PinnedWithEmptyList_SelectsMissing()
    {
        Assert.Equal(SessionModelSelectionKind.Missing,
            SessionModelOption.ResolveSelection("m1", []));
    }
}
