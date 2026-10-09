using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 本地引擎的发布版本管理：拉 GitHub releases、筛本平台包、解压到引擎目录，以找到服务端程序为安装成功的判据。
/// 各引擎只回答「仓库在哪、服务端叫什么、哪些包不列」
/// </summary>
public abstract class EngineVersionManager<TPackage> : ReleaseVersionManagerBase<TPackage>
    where TPackage : EnginePackage, new()
{
    /// <summary>
    /// 服务端可执行文件名（不含扩展名）
    /// </summary>
    public abstract string ServerExeName { get; }

    /// <summary>
    /// 本机已装的版本（引擎目录里扫）
    /// </summary>
    /// <param name="rootDirectory">引擎目录</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>版本快照</returns>
    public virtual Task<IReadOnlyList<TPackage>> ScanLocalAsync(string rootDirectory,
        CancellationToken cancellationToken = default) =>
        GetLocalVersionsAsync(rootDirectory, cancellationToken: cancellationToken);

    protected override void ConfigureLocalVersion(TPackage version, string versionDirectory)
    {
        version.ExecutablePath = versionDirectory;
    }

    // 包可能嵌一层目录，服务端程序在哪层就以哪层为可执行目录
    protected override ManagedVersionValidationResult ValidateInstalledVersion(TPackage version)
    {
        if (!Directory.Exists(version.InstallDirectory))
            return new ManagedVersionValidationResult(false, "Runtime directory does not exist.");

        foreach (string file in Directory.EnumerateFiles(
                     version.InstallDirectory, ServerExeName + "*", SearchOption.AllDirectories))
        {
            version.ExecutablePath = Path.GetDirectoryName(file)!;
            EnsureExecutable(file);
            return ManagedVersionValidationResult.Valid;
        }

        return new ManagedVersionValidationResult(false, $"{ServerExeName} was not found.");
    }

    // 有的包压缩时没带权限位（sd.cpp 的 zip 就是），解出来不可执行，起进程报 126：补上可执行位
    private static void EnsureExecutable(string file)
    {
        if (OperatingSystem.IsWindows()) return;
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        UnixFileMode mode = File.GetUnixFileMode(file);
        if ((mode & UnixFileMode.UserExecute) == 0) File.SetUnixFileMode(file, mode | execute);
    }

    // 发布说明夹着 HTML（<details> 等），整理成 markdown 交给界面渲染
    protected override string CreateReleaseInfoText(GitHubReleaseInfo release)
    {
        string? releaseDate = release.PublishedAt.HasValue
            ? TimeUtils.TimeStringToLocalTimeString(release.PublishedAt.Value.ToString("O"))
            : null;
        string body = MarkdownCleaner.Clean(release.Body ?? "");
        string title = releaseDate == null ? $"**{release.TagName}**" : $"**{release.TagName}** · {releaseDate}";
        return $"{title}\n\n{body}".TrimEnd();
    }
}
