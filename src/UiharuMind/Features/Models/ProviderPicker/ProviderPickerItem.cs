using System;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ProviderPicker;

/// <summary>选择器里的一行：供应商本体 + 是否已内置 + 点击即选中</summary>
/// <param name="Provider">供应商</param>
/// <param name="IsBuiltIn">左侧列表里已经有这家（点了会直接跳到那家预设行）</param>
/// <param name="PickCommand">点击即选中</param>
public sealed record ProviderPickerItem(RemoteProviderInfo Provider, bool IsBuiltIn, IRelayCommand PickCommand)
{
    /// <summary>显示名</summary>
    public string Name => Provider.Name;

    /// <summary>
    /// 字母圆牌：不联网取 logo（models.dev 有 <c>/logos/{id}.svg</c>，但 200 多个请求换不来多少信息，
    /// 离线还会花屏）
    /// </summary>
    public string Initial => Provider.Name.Length > 0
        ? Provider.Name[..1].ToUpperInvariant()
        : Provider.Id[..1].ToUpperInvariant();

    /// <summary>副行：id + 模型数。模型数是挑之前判断「值不值得配」最快的信号</summary>
    public string Subtitle =>
        $"{Provider.Id} · {string.Format(Loc.Text(LangKey.ProviderPickerModelCount), Provider.ModelCount)}";

    /// <summary>点选之后填进表单的接口地址，放 ToolTip 里，免得选完才发现不是自己要的那家</summary>
    public string EndpointHint => Provider.ApiBaseUrl;
}
