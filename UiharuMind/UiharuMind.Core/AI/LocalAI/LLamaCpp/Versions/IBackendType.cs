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

namespace UiharuMind.Core.LLamaCpp.Versions;

public interface IBackendType
{
    string Name { get; }

    bool IsAvailable { get; }

    // 执行特定操作
    void ExecuteOperation();
}