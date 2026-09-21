using Avalonia.Media;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 工作区项目色映射:同路径恒同色、换路径换色、深浅主题亮度分开、无工作区恒灰。
/// 这是列表上「一眼区分项目」的根基,色不稳定会把人认错项目。
/// </summary>
public class WorkspaceTintTests
{
    [Fact]
    public void SameWorkspace_AlwaysSameColor()
    {
        SolidColorBrush a = WorkspaceTint.For("/Users/me/projects/client", dark: false);
        SolidColorBrush b = WorkspaceTint.For("/Users/me/projects/client", dark: false);

        Assert.Equal(a.Color, b.Color);
        Assert.Same(a, b); //缓存同一实例
    }

    [Fact]
    public void DifferentWorkspaces_NormallyDiffer()
    {
        SolidColorBrush client = WorkspaceTint.For("/Users/me/projects/client", dark: false);
        SolidColorBrush server = WorkspaceTint.For("/Users/me/projects/server", dark: false);

        Assert.NotEqual(client.Color, server.Color);
    }

    /// <summary>
    /// 哈希的是<b>完整路径</b>而不是目录名:同名目录在不同父路径下必须能区分。
    /// 只 hash 目录名的话,这里的两个项目会撞成同一种色
    /// </summary>
    [Fact]
    public void SameFolderName_DifferentParents_Differ()
    {
        SolidColorBrush a = WorkspaceTint.For("/a/projects/client", dark: false);
        SolidColorBrush b = WorkspaceTint.For("/b/projects/client", dark: false);

        Assert.NotEqual(a.Color, b.Color);
    }

    [Fact]
    public void DarkTheme_UsesBrighterTextColor()
    {
        SolidColorBrush light = WorkspaceTint.For("/Users/me/projects/client", dark: false);
        SolidColorBrush dark = WorkspaceTint.For("/Users/me/projects/client", dark: true);

        Assert.True(Brightness(dark.Color) > Brightness(light.Color),
            "深色主题该用更亮的字,现在是 light=" + Brightness(light.Color) + ", dark=" + Brightness(dark.Color));
    }

    [Fact]
    public void NoWorkspace_IsNeutralGrey_RegardlessOfTheme()
    {
        Assert.True(IsGrey(WorkspaceTint.For(null, dark: false).Color));
        Assert.True(IsGrey(WorkspaceTint.For("", dark: true).Color));
        Assert.True(IsGrey(WorkspaceTint.For("   ", dark: true).Color));
    }

    private static bool IsGrey(Color c) => c.R == c.G && c.G == c.B;

    private static double Brightness(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}