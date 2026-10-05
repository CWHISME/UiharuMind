namespace UiharuMind.Core.AI.Chat.CrossProcess;

/// <summary>
/// 文件的盘上指纹：修改时间 + 长度。两次取到的不一样，就是中间有人写过
/// </summary>
/// <param name="LastWriteUtc">最后修改时间</param>
/// <param name="Length">字节数；文件不在为 -1</param>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
{
    /// <summary>文件不存在时的指纹</summary>
    public static readonly FileStamp Missing = new(DateTime.MinValue, -1);

    /// <summary>
    /// 取一个文件此刻的指纹
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>指纹；文件不在或读不到为 <see cref="Missing"/></returns>
    public static FileStamp Of(string path)
    {
        try
        {
            FileInfo info = new(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : Missing;
        }
        catch (Exception)
        {
            return Missing;
        }
    }
}
