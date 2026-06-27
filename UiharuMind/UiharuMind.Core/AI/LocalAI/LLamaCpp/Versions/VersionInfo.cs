/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.LLamaCpp.Versions;

public class VersionInfo : ManagedVersionPackage
{
    /// <summary>
    /// llama.cpp 服务端可执行文件所在目录；与安装根目录分开，避免嵌套包覆盖安装路径。
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;
}
