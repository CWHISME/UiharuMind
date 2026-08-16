using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.CharacterCards;

public static class CharacterCardImporter
{
    public static CharacterCard Import(string json)
    {
        var card = SaveUtility.LoadFromString<CharacterCard>(json);
        return card;
    }

    /// <summary>示例对话拼进提示词时的小标题（原先它是角色的独立字段，见 ADR 0015）</summary>
    private const string DialogExampleHeader = "Dialog Template:";

    public static async Task<CharacterData?> ImportToCharactorData(string json)
    {
        var card = Import(json);
        if (card.Data == null) return null;
        var data = card.Data;
        var charactorData = new CharacterData
        {
            CharacterName = data.Name ?? "",
            // 示例对话直接拼进提示词：它本来就只是提示词的一段，
            // 单独存一个字段的时候它折在高级选项里，等于"编辑页看不见却每轮都发出去"
            Template = AppendDialogExample(data.Description ?? "", data.MesExample),
            FirstGreeting = data.FirstMes ?? "",
            Description =
                $"Ceator:{data.Creator ?? "*"}\n***\n\n{data.CreatorNotes ?? "*"}",
        };
        if (!string.IsNullOrEmpty(data.Avatar))
        {
            var bytes = await SimpleDownloadHelper.DownloadFileAsync(data.Avatar);
            if (bytes != null) charactorData.CharacterIcon = Convert.ToBase64String(bytes);
        }

        return charactorData;
    }

    /// <summary>
    /// 把角色卡的示例对话追加到提示词末尾
    /// </summary>
    /// <param name="template">卡上的角色描述</param>
    /// <param name="dialogExample">卡上的示例对话；为空则原样返回</param>
    /// <returns>拼接后的提示词</returns>
    private static string AppendDialogExample(string template, string? dialogExample)
    {
        if (string.IsNullOrWhiteSpace(dialogExample)) return template;
        string block = $"{DialogExampleHeader}\n{dialogExample}";
        return string.IsNullOrWhiteSpace(template) ? block : $"{template}\n\n{block}";
    }
}