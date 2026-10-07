using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.WorldSettings;
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
            Template = AppendPostHistory(AppendDialogExample(data.Description ?? "", data.MesExample),
                data.PostHistoryInstructions),
            FirstGreeting = data.FirstMes ?? "",
            AlternateGreetings = CoerceGreetings(data.AlternateGreetings),
            DepthPrompt = data.Extensions?.DepthPrompt is { } depthPrompt
                ? new DepthPromptInfo
                {
                    Prompt = depthPrompt.Prompt ?? "",
                    Depth = depthPrompt.Depth ?? 0,
                    Role = depthPrompt.Role ?? "",
                }
                : null,
            WorldSettingName = ImportCharacterBook(data.CharacterBook, data.Name ?? ""),
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
    /// 把角色卡内嵌的 character_book 落成（或并入）一份以角色命名的世界书并挂载。
    /// 返回挂载名；无书或书内无条目返回空串。
    /// </summary>
    private static string ImportCharacterBook(CharacterBook? book, string characterName)
    {
        WorldSetting entries = FromCharacterBook(book);
        if (entries.Entries.Count == 0) return "";

        string bookName = $"{characterName}·世界书";
        WorldSettingManager.Instance.Merge(bookName, entries.Entries);
        return bookName;
    }

    /// <summary>
    /// 把角色卡内嵌的 character_book 映射成世界设定条目集合（起步版：keys / constant / insertion_order / position / content）。
    /// 无书或书内无条目时返回空集合。
    /// </summary>
    private static WorldSetting FromCharacterBook(CharacterBook? book)
    {
        WorldSetting setting = new();
        if (book?.Entries is not { Count: > 0 }) return setting;

        for (int i = 0; i < book.Entries.Count; i++)
        {
            CharacterBookEntry entry = book.Entries[i];
            if (entry.Enabled == false) continue;
            setting.Entries.Add(new WorldSettingEntry
            {
                Keys = entry.Keys ?? [],
                Constant = entry.Constant == true,
                Position = (entry.Position ?? "") switch
                {
                    "after_char" => EWorldSettingPosition.AfterCharacter,
                    _ => EWorldSettingPosition.BeforeCharacter,
                },
                Order = entry.InsertionOrder ?? i,
                Content = entry.Content ?? "",
            });
        }

        return setting;
    }

    /// <summary>
    /// 把角色卡的 post_history_instructions 并入提示词末尾：它本来就是要发给模型的一段指令，
    /// 不设独立字段——那种做法让它与人格锚机制重复（装配里既要它能生效又得防它干扰顺序）。空则原样返回。
    /// </summary>
    private static string AppendPostHistory(string template, string? postHistory)
    {
        if (string.IsNullOrWhiteSpace(postHistory)) return template;
        string block = postHistory.Trim();
        return string.IsNullOrWhiteSpace(template) ? block : $"{template.TrimEnd()}\n\n{block}";
    }

    /// <summary>
    /// 把卡上的 alternate_greetings 收拢成字符串列表：规范上是字符串数组，
    /// 但乱写的卡（数字/对象/布尔项）不该让整张导入失败——非字符串项丢弃。
    /// </summary>
    private static List<string> CoerceGreetings(List<object>? greetings)
    {
        if (greetings == null) return [];

        List<string> result = [];
        foreach (object? item in greetings)
        {
            // 本仓的 JsonOptions 带 UnknownTypeHandling=JsonNode，object 元素是 JsonValue 不是 string/JsonElement
            string? text = item switch
            {
                string s => s,
                JsonValue { } v when v.TryGetValue<string>(out string? s) => s,
                _ => null,
            };
            if (text != null) result.Add(text);
        }

        return result;
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