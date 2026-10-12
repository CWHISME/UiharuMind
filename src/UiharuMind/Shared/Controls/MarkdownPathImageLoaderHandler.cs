using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using LiveMarkdown.Avalonia;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 本地图片加载：先经 <see cref="MarkdownPathMapper"/> 把哨兵拼出的路径还原成真实路径，再读文件。
/// 只走渲染库公开的加载器扩展点，不改库（ADR 0080）。
///
/// 加载器经样式挂到图片上，而渲染库在图片进树<b>之前</b>就设了地址——那一刻用的还是默认加载器，
/// 样式生效后库也不会重新加载。所以加载器一换成本类，就把地址重设一次，按新加载器再加载。
/// </summary>
public sealed class MarkdownPathImageLoaderHandler : AsyncImageLoaderHandler
{
    private readonly MarkdownPathMapper _mapper;

    static MarkdownPathImageLoaderHandler()
    {
        AsyncImageLoader.HandlersProperty.Changed.AddClassHandler<Image>(ReloadWithMappedHandler);
    }

    /// <summary>
    /// 构造加载器
    /// </summary>
    /// <param name="mapper">会话的路径映射</param>
    public MarkdownPathImageLoaderHandler(MarkdownPathMapper mapper)
    {
        _mapper = mapper;
    }

    /// <inheritdoc/>
    public override IEnumerable<string> SupportedSchemes => ["file"];

    /// <inheritdoc/>
    public override Task<Stream> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            Stream stream = File.OpenRead(_mapper.MapLocalPath(uri.LocalPath));
            return Task.FromResult(stream);
        }
        catch (Exception e)
        {
            // 地址来自不可信正文：缺文件、无权限、超长路径都只让这张图加载失败。
            // 返回 faulted Task 而不是同步抛，合异步加载器的契约
            Log.Warning($"Image load failed '{uri.LocalPath}': {e.Message}");
            return Task.FromException<Stream>(e);
        }
    }

    private static void ReloadWithMappedHandler(Image image, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is not IReadOnlyCollection<AsyncImageLoaderHandler> handlers ||
            !handlers.OfType<MarkdownPathImageLoaderHandler>().Any()) return;
        if (AsyncImageLoader.GetSource(image) is not { } source) return;

        AsyncImageLoader.SetSource(image, null);
        AsyncImageLoader.SetSource(image, source);
    }

    /// <summary>
    /// 替换掉默认本地文件加载器后的整套加载器
    /// </summary>
    /// <param name="mapper">会话的路径映射</param>
    /// <returns>加载器列表，顺序与渲染库默认一致</returns>
    public static IReadOnlyCollection<AsyncImageLoaderHandler> HandlersFor(MarkdownPathMapper mapper) =>
    [
        HttpAsyncImageLoaderHandler.Shared,
        new MarkdownPathImageLoaderHandler(mapper),
        AvaloniaResourceAsyncImageLoaderHandler.Shared,
        DataUrlAsyncImageLoaderHandler.Shared,
        RawAsyncImageLoaderHandler.Shared,
    ];
}
