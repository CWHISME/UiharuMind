namespace UiharuMind.Core.AI.WorldSettings;

/// <summary>
/// 世界设定条目的注入位置。相对角色人格正文：前 = 先交代世界再交代角色，后 = 反之。
/// 起步版在同一注入块内分组表达，不拆多个 provider。
/// </summary>
public enum EWorldSettingPosition
{
    BeforeCharacter = 0,
    AfterCharacter = 1,
}

/// <summary>一条世界设定：关键词命中后注入的文本块。</summary>
public sealed class WorldSettingEntry
{
    /// <summary>触发关键词。任一命中即激活（起步版，多级关键词逻辑是后续里程碑）。</summary>
    public List<string> Keys { get; set; } = [];

    /// <summary>常驻：不依赖关键词，每轮都注入。</summary>
    public bool Constant { get; set; }

    /// <summary>注入位置：角色人格前/后。</summary>
    public EWorldSettingPosition Position { get; set; } = EWorldSettingPosition.BeforeCharacter;

    /// <summary>同位置内的注入顺序（小者在前）。</summary>
    public int Order { get; set; }

    /// <summary>正文。</summary>
    public string Content { get; set; } = "";
}

/// <summary>
/// 一份世界设定：条目集合与注入预算。挂在角色上（角色卡内嵌 character_book 导入后即此），
/// 每轮由 <see cref="WorldSettingContextProvider"/> 按关键词激活注入。
/// 只存数据，选择与排版在 <see cref="WorldSettingSelector"/>。
/// </summary>
public sealed class WorldSetting
{
    public List<WorldSettingEntry> Entries { get; set; } = [];

    /// <summary>一次注入的 token 预算，与知识库检索同款口径（<see cref="WorldSettingSelector.DefaultTokenBudget"/>）。</summary>
    public int TokenBudget { get; set; } = WorldSettingSelector.DefaultTokenBudget;
}