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

using Microsoft.SemanticKernel;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.AI.Interfaces;

public interface ILlmRuntimeEngine
{
    public Task Run(VersionInfo info, ILlmModel model, Action<float>? onLoading = null,
        Action<Kernel>? onLoadOver = null, int? port = null, string extraParams = "",
        CancellationToken token = default);
}