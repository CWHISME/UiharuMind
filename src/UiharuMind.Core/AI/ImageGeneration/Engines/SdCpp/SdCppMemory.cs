namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// sd-server 起来后大约占多少内存：权重启动时全读进来（--eager-load），再加采样与 VAE 解码的工作区。
/// 是拉起前检查与下载前风险徽标共用的粗估，不是 llama 那套按上下文算 KV 缓存的估法
/// </summary>
public static class SdCppMemory
{
    private const long WorkspaceBytes = 2L * 1024 * 1024 * 1024; //1024² 一张的采样与解码工作区，粗估

    /// <summary>
    /// 按权重总大小估
    /// </summary>
    /// <param name="weightBytes">各组件文件大小之和</param>
    /// <returns>估的占用（字节）</returns>
    public static long Estimate(long weightBytes) => weightBytes + WorkspaceBytes;

    /// <summary>
    /// 按本机的组件文件估（缺的文件不算）
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>估的占用（字节）</returns>
    public static long Estimate(SdCppModelSetup model) => Estimate(model.Components
        .Select(x => new FileInfo(x.Path))
        .Where(x => x.Exists)
        .Sum(x => x.Length));
}
