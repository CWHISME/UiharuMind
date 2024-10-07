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

namespace UiharuMind.Core.AI.Interfaces;

public interface ILlmRuntime
{
    public Task Run(ILlmModel model, Action<float>? onLoading = null, Action? onLoadOver = null,CancellationToken token = default);
}