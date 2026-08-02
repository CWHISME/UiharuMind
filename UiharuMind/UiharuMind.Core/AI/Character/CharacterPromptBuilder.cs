/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 把一个角色装配成系统提示词的<b>唯一</b>实现。
/// 主对话、技能、agent 三条路径都走这里——此前 ChatSession.BuildRequestMessagesAsync 与
/// CharacterConfig.ToAgent 各有一套装配逻辑，导致同一角色在聊天页和在技能里
/// 看到的系统提示并不相同(后者完全忽略挂载片段与对话模板)。
/// </summary>
public static class CharacterPromptBuilder
{
    private const string DialogTemplateHeader = "Dialog Template:";

    /// <summary>
    /// 装配系统提示词：按顺序拼接内联挂载片段 → 本角色 Template → 对话模板
    /// </summary>
    /// <param name="character">目标角色</param>
    /// <param name="arguments">额外的模板参数(会与角色的公共参数合并，不会被修改)</param>
    /// <returns>渲染完成的系统提示词；无内容时为空串</returns>
    public static string Build(CharacterData character, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        // 复制一份再补公共参数：直接补进调用方的字典会污染它
        // (旧实现把 lang/char/user 写进了 ChatSession.CustomParams,并随会话一起持久化)
        Dictionary<string, object?> args = arguments == null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(arguments);
        character.BuildPromptArguments(args);

        StringBuilder sb = StringBuilderPool.Get();
        try
        {
            foreach (string mountId in character.MountPrompts)
            {
                CharacterData mounted = CharacterManager.Instance.GetCharacterData(mountId);
                if (string.IsNullOrEmpty(mounted.Template)) continue;
                AppendBlock(sb, CharacterPromptRenderer.Render(mounted.Template, args));
            }

            AppendBlock(sb, CharacterPromptRenderer.Render(character.Template, args));

            if (!string.IsNullOrEmpty(character.DialogTemplate))
            {
                sb.AppendLine(DialogTemplateHeader);
                AppendBlock(sb, CharacterPromptRenderer.Render(character.DialogTemplate, args));
            }

            return sb.ToString().TrimEnd();
        }
        finally
        {
            StringBuilderPool.Release(sb);
        }
    }

    private static void AppendBlock(StringBuilder sb, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.AppendLine(text);
        sb.AppendLine();
    }
}
