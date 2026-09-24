namespace UiharuMind.Core.AI.Character.PromptActions;

/// <summary>
/// 一次视觉问答的一张输入图：字节 + MIME 类型。
/// 多图对比时给视觉模型同框看多张（见 VisionTool 的 imagePaths 参数）。
/// </summary>
/// <param name="Bytes">图片字节</param>
/// <param name="MediaType">图片 MIME 类型</param>
public readonly record struct ImageInput(byte[] Bytes, string MediaType);
