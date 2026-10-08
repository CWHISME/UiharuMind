using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 一份下载下来的本地引擎成品包（某个发布号 × 某个变体）。各引擎的差别只在哪个变体算本机推荐
/// </summary>
public abstract class EnginePackage : ManagedVersionPackage
{
    /// <summary>
    /// 服务端可执行文件所在目录；与安装根目录分开，避免嵌套包覆盖安装路径
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否本机的推荐变体
    /// </summary>
    public abstract bool IsRecommended { get; }

    /// <summary>
    /// 包里某个可执行文件的完整路径（Windows 自动补 .exe）
    /// </summary>
    /// <param name="name">文件名，不含扩展名</param>
    /// <returns>路径</returns>
    public string GetExecutable(string name) =>
        Path.Combine(ExecutablePath, OperatingSystem.IsWindows() ? name + ".exe" : name);

    /// <summary>
    /// 推荐变体排前面，其余按版本
    /// </summary>
    public override int CompareTo(ManagedVersionPackage? other)
    {
        if (other is EnginePackage package && package.IsRecommended != IsRecommended) return IsRecommended ? -1 : 1;
        return base.CompareTo(other);
    }
}
