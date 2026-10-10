using System;
using System.IO;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 回复正文里文件地址的解析口径（ADR 0080）：模型在正文里写 <c>$DRAFT/x.png</c> 或工作区相对路径，
/// 历史里原样存，显示时按会话的 <see cref="AgentPathResolver"/> 解析成可加载、可点开的本地路径。
///
/// 图片不经点击而是渲染时就加载，而渲染库只会把相对地址与 <see cref="ImageBasePath"/> 拼成绝对路径。
/// 所以基准给一个<b>永不存在的哨兵目录</b>：拼出来的路径落在它下面，就切回原写法再解析；
/// 落在外面的是本来就写了绝对地址的旧消息，原样用。哨兵挂在草稿目录下，
/// 拼出的地址因此每个会话各不相同，图片缓存不会把甲会话的 x.png 给乙会话用。
/// </summary>
public sealed class MarkdownPathMapper
{
    private const string SentinelName = ".markdown-links"; //永不建出来的目录，只当拼接基准

    private readonly AgentPathResolver _resolver;

    private MarkdownPathMapper(AgentPathResolver resolver, string imageBasePath)
    {
        _resolver = resolver;
        ImageBasePath = imageBasePath;
    }

    /// <summary>交给渲染库的图片基准（哨兵目录）</summary>
    public string ImageBasePath { get; }

    /// <summary>
    /// 按会话路径口径建映射
    /// </summary>
    /// <param name="resolver">会话的路径口径</param>
    /// <returns>映射；既没有草稿目录也没有工作区时为 null（没有可解析的参照系）</returns>
    public static MarkdownPathMapper? Create(AgentPathResolver resolver)
    {
        string root = resolver.DraftRoot.Length > 0 ? resolver.DraftRoot : resolver.WorkspaceRoot;
        return root.Length == 0 ? null : new MarkdownPathMapper(resolver, Path.Combine(root, SentinelName));
    }

    /// <summary>
    /// 解析正文里的相对地址（简写或工作区相对路径）
    /// </summary>
    /// <param name="link">链接原文</param>
    /// <returns>本地绝对路径；纯锚点、解析不了时为 null</returns>
    public string? ResolveLink(string link)
    {
        string path = StripAnchor(Uri.UnescapeDataString(link));
        if (path.Length == 0) return null;

        try
        {
            return _resolver.Resolve(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 把渲染库拼过基准的本地路径还原成真实路径
    /// </summary>
    /// <param name="localPath">渲染库给出的本地路径</param>
    /// <returns>真实路径；不是经哨兵拼出来的原样返回</returns>
    public string MapLocalPath(string localPath)
    {
        string relative = Path.GetRelativePath(ImageBasePath, localPath);
        if (Path.IsPathFullyQualified(relative)) return localPath;
        // 落在哨兵外面：多半是写了绝对地址的旧消息；也可能是 ../x 这种相对写法，以磁盘上有没有为准
        if (relative.StartsWith("..", StringComparison.Ordinal) && (File.Exists(localPath) || Directory.Exists(localPath)))
        {
            return localPath;
        }

        return ResolveLink(relative) ?? localPath;
    }

    /// <summary>剥掉 #锚点：它不是路径的一部分，否则 File.Exists 会把 "x.md#锚点" 整个当文件名</summary>
    /// <param name="link">链接原文</param>
    /// <returns>去掉锚点的部分</returns>
    public static string StripAnchor(string link)
    {
        int hash = link.IndexOf('#');
        return hash >= 0 ? link[..hash] : link;
    }
}
