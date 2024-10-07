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

using Tiktoken;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI;

public static class LlmTokenizer
{
    private static string _modelName = "";
    private static Encoder? _modelEncoder;

    /// <summary>
    /// 计算指定字符串的 Token 数量
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static int GetInputTokenCount(string input)
    {
        if (_modelName != ConfigManager.Instance.ChatSetting.TokenForModelName)
            _modelEncoder = ModelToEncoder.For(ConfigManager.Instance.ChatSetting.TokenForModelName);
        if (_modelEncoder == null)
        {
            _modelEncoder = ModelToEncoder.For("gpt-4o");
            ConfigManager.Instance.ChatSetting.TokenForModelName = "gpt-4o";
            ConfigManager.Instance.ChatSetting.Save();
        }

        // var tokens = _modelEncoder.Encode(input); // [15339, 1917]
        // var text = _modelEncoder.Decode(tokens); // hello world
        // var stringTokens = _modelEncoder.Explore(input); // ["hello", " world"]
        return _modelEncoder.CountTokens(input); // 2
    }
}