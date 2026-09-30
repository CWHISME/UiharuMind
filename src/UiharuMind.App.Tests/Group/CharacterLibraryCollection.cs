namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 会增删或遍历角色库（<c>CharacterManager.Instance.CharacterDataDictionary</c>）的测试类挂这里、彼此串行：
/// 一边临时加卡、删卡一边被另一个类遍历，会随机抛「集合在遍历时被修改」，挑出来的卡也会混进试卡
/// </summary>
[CollectionDefinition(Name)]
public sealed class CharacterLibraryCollection
{
    public const string Name = "CharacterLibrary";
}
