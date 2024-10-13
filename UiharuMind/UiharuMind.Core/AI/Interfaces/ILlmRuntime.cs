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

namespace UiharuMind.Core.AI.Interfaces;

public interface ILlmRuntime
{
    public Task Run(ILlmModel model, Action<float>? onLoading = null, Action<Kernel>? onLoadOver = null,
        CancellationToken token = default);
}