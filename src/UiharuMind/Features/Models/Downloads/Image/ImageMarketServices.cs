using System;
using System.Collections.Generic;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads.Image;

/// <summary>
/// 生图分类要的：引擎（没装时一键装）、组件的下载与安装状态、生图模型列表、可下载列表，以及开画图窗口、开网页
/// </summary>
/// <param name="Engine">生图引擎的分发（装没装、一键装）</param>
/// <param name="Store">组件的下载与安装状态</param>
/// <param name="Catalog">可下载列表</param>
/// <param name="Models">生图模型列表（现取）</param>
/// <param name="AddModel">把装好的模型加进列表末尾</param>
/// <param name="OpenDraw">打开画图窗口</param>
/// <param name="OpenUrl">打开网页</param>
public sealed record ImageMarketServices(
    EngineDistribution<SdCppVersion> Engine,
    LocalImageModelStore Store,
    ImageModelCatalog Catalog,
    Func<IReadOnlyList<ImageModelInfo>> Models,
    Action<ImageModelInfo> AddModel,
    Action<ImageModelInfo?> OpenDraw,
    Action<string> OpenUrl)
{
    /// <summary>
    /// 界面语言（家族简介按它取）
    /// </summary>
    public static string Language => LocalizationManager.Instance.LanguageCode.ToLowerInvariant();
}
