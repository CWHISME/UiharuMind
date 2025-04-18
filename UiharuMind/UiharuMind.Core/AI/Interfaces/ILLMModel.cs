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

public interface ILlmModel
{
    string ModelName { get; }
    string ModelPath { get; }
    bool IsVision { get; }
    string ModelDescription { get; }
    string ModelId { get; }
    int Port { get; }
    string ApiKey { get; }
}