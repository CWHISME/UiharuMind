using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 生图模型列表。顺序即回退顺序，全局一份。
/// </summary>
public sealed class ImageModelSettingConfig : TConfigBase<ImageModelSettingConfig>
{
    /// <summary>生图模型，靠前的先试。改动走 <see cref="ReplaceModels"/>，不要就地改</summary>
    public List<ImageModelInfo> Models { get; set; } = new();

    /// <summary>列表换了一份（增删改、排序）</summary>
    public event Action? ModelsChanged;

    /// <summary>
    /// 整体换上一份新列表并落盘。写时复制：出图在线程池上遍历旧的那份，就地改会撞上「集合已修改」
    /// </summary>
    /// <param name="models">新列表，调用方之后不再改它</param>
    public void ReplaceModels(List<ImageModelInfo> models)
    {
        Models = models;
        Save();
        ModelsChanged?.Invoke();
    }
}
