namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 会重建角色库单例（<c>CharacterManager.Instance.OnInitialize()</c>）的测试类挂这里、彼此串行：
/// 一边重建一边被另一个类遍历，会随机抛「集合在遍历时被修改」
/// </summary>
[CollectionDefinition(Name)]
public sealed class CharacterLibraryCollection
{
    public const string Name = "CharacterLibrary";
}
