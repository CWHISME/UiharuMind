/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 上传的文件从哪来：只认工作区与草稿目录里已有的文件。上传是把本机文件交给第三方网站，
/// 主目录里的密钥、别的项目一概够不着；软链按它指向的真实位置判，免得工作区里一个链接把 ~/.ssh 带出去。
/// </summary>
public static class BrowserUploadFiles
{
    /// <summary>
    /// 把模型给的路径解析成要上传的文件
    /// </summary>
    /// <param name="value">一个或多个路径，换行分隔</param>
    /// <param name="paths">路径口径</param>
    /// <returns>绝对路径</returns>
    /// <exception cref="BrowserActionException">没给路径、解析不了、不是已有文件、或不在工作区与草稿目录里</exception>
    public static IReadOnlyList<string> Resolve(string value, AgentPathResolver paths)
    {
        string[] given = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (given.Length == 0)
        {
            throw new BrowserActionException("upload needs value: the path of the file to upload (one per line).");
        }

        List<string> files = new();
        foreach (string path in given)
        {
            if (!paths.TryResolve(path, out string full, out string error))
            {
                throw new BrowserActionException($"Cannot upload \"{path}\": {error}");
            }

            if (!File.Exists(full)) throw new BrowserActionException($"Cannot upload \"{path}\": no such file.");
            if (!IsAllowed(full, paths) ||
                File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target && !IsAllowed(target.FullName, paths))
            {
                throw new BrowserActionException($"Cannot upload \"{path}\": only files in the workspace or " +
                                                 $"{paths.DraftToken} can be uploaded. Copy it there first if the user wants it sent.");
            }

            files.Add(full);
        }

        return files;
    }

    private static bool IsAllowed(string full, AgentPathResolver paths) =>
        AgentPathResolver.IsUnder(full, paths.WorkspaceRoot) || AgentPathResolver.IsUnder(full, paths.DraftRoot);
}
