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

    public static async Task<CharacterData?> ImportToCharactorData(string json)
    {
        var card = Import(json);
        if (card.Data == null) return null;
        var data = card.Data;
        var charactorData = new CharacterData
        {
            CharacterName = data.Name ?? "",
            Template = data.Description ?? "",
            FirstGreeting = data.FirstMes ?? "",
            DialogTemplate = data.MesExample ?? "",
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
}