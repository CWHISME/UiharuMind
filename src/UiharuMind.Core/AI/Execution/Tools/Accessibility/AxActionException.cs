/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Accessibility;

/// <summary>
/// 写给模型看的动作失败原因（编号过期、找不到元素、拒绝往密码框打字），工具门面原样交回
/// </summary>
/// <param name="message">原因</param>
public class AxActionException(string message) : Exception(message);
