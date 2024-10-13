using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Character.CharacterCards;

public static class CharacterCardImporter
{
    public static CharacterCard Import(string json)
    {
        var card = SaveUtility.LoadFromString<CharacterCard>(json);
        return card;
    }
}