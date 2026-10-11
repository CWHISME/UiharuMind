/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace UiharuMind.Platform.Windows.Ocr;

/// <summary>
/// OneOCR 的三个文件：只从本机已装的截图工具或照片应用里找，复制进缓存目录再用（ADR 0084），<b>绝不从网上下载</b>。
/// 复制一份是因为应用更新时会换掉原来的安装目录
/// </summary>
internal static class OneOcrFiles
{
    public const string Library = "oneocr.dll";
    public const string Model = "oneocr.onemodel";
    public const string Runtime = "onnxruntime.dll";

    private static readonly string[] Files = [Library, Model, Runtime];

    // 截图工具在前（社区实测最多），照片应用里也带一套
    private static readonly string[] PackageFamilies = ["Microsoft.ScreenSketch_8wekyb3d8bbwe", "Microsoft.Windows.Photos_8wekyb3d8bbwe"];

    /// <summary>
    /// 找到并备好文件
    /// </summary>
    /// <returns>放着三个文件的目录；本机没有为 null</returns>
    public static string? Prepare()
    {
        PackageManager manager = new();
        foreach (string family in PackageFamilies)
        {
            foreach (Package package in manager.FindPackagesForUser(string.Empty, family))
            {
                string? source = SourceDirectory(package.InstalledLocation.Path);
                if (source == null) continue;
                string target = Path.Combine(AppPaths.Cache.Root, "OneOcr", package.Id.FullName);
                if (!Files.All(file => File.Exists(Path.Combine(target, file))))
                {
                    Directory.CreateDirectory(target);
                    foreach (string file in Files) File.Copy(Path.Combine(source, file), Path.Combine(target, file), overwrite: true);
                    Log.Debug($"OneOCR files copied from {package.Id.FullName}");
                }

                return target;
            }
        }

        return null;
    }

    // 三个文件在安装目录的某个子目录里（截图工具是 SnippingTool\），三个要在一起
    private static string? SourceDirectory(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, Library, SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(directory => directory != null && Files.All(file => File.Exists(Path.Combine(directory, file))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
