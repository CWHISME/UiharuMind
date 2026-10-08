namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 一个组件怎么交给 sd-server
/// </summary>
/// <param name="Flag">启动参数名，不带横线（如 diffusion-model、vae、llm、clip_l、t5xxl）</param>
/// <param name="Path">文件绝对路径</param>
public sealed record SdCppComponent(string Flag, string Path);

/// <summary>
/// 一个本地生图模型要怎么起 sd-server：各组件加上这个家族要的固定参数。
/// sd-server 一个进程只装一个模型，换模型就是换进程，<see cref="Key"/> 一样才算同一个
/// </summary>
/// <param name="Name">显示名（日志与报错里用）</param>
/// <param name="Components">组件</param>
/// <param name="ExtraArguments">家族要的固定参数（如 --vae-format）</param>
public sealed record SdCppModelSetup(string Name, IReadOnlyList<SdCppComponent> Components,
    IReadOnlyList<string> ExtraArguments)
{
    /// <summary>
    /// 起进程用到的全部东西拼成的键
    /// </summary>
    public string Key { get; } = string.Join('\n',
        Components.Select(x => $"{x.Flag}={x.Path}").Concat(ExtraArguments));

    /// <summary>
    /// 缺了哪些文件（被手动删了、还没下完）；都在为空
    /// </summary>
    public IReadOnlyList<string> MissingFiles() => Components.Select(x => x.Path).Where(x => !File.Exists(x)).ToList();
}
