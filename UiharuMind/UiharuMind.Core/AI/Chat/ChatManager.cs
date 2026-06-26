using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Core.Chat;

public class ChatManager : UniquieContainerSingleton<ChatManager, ChatSession>
{
    public override void OnInitialize()
    {
        ItemDictionary.Clear();
        if (!Directory.Exists(SaveRootPath)) return;

        foreach (string file in Directory.GetFiles(SaveRootPath, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                // 本次迁移不兼容旧 SK 消息格式；缺少版本号的历史会话直接跳过。
                if (!json.Contains("\"FormatVersion\"", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning($"Skip legacy chat session: {file}");
                    continue;
                }

                ChatSession? session = SaveUtility.Load<ChatSession>(file);
                if (session is not { FormatVersion: 2 }) continue;
                session.Name = Path.GetFileNameWithoutExtension(file);
                ItemDictionary[session.Name] = session;
            }
            catch (Exception e)
            {
                Log.Warning($"Skip invalid chat session '{file}': {e.Message}");
            }
        }
    }

    public ChatSession StartNewSession(CharacterData characterData)
    {
        var session = new ChatSession(GetUniqueName(characterData.CharacterName), characterData);
        Add(session);
        return session;
    }

    protected override string SaveRootPath => SettingConfig.SaveChatDataPath;

    protected override void OnOrderedItems(List<ChatSession> items)
    {
        items.Sort((x, y) => y.LastTime.CompareTo(x.LastTime));
    }
}
