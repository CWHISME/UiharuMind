/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 一条流的追加写入与按大小滚动。<b>非线程安全</b>，由 <see cref="LogStore"/> 持锁调用。
///
/// 滚动只是改名（<c>Log.txt → Log.1.txt</c>），因此<b>已记下的偏移量依然有效</b>；
/// 索引项带上 <c>FileId</c> 就能继续读到旧文件里的正文。
/// </summary>
internal sealed class LogFileWriter : IDisposable
{
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _generations; //含当前代
    private Stream? _stream;
    private long _offset; //当前文件已写入的字节数,也就是下一条的起点
    private int _currentFileId;

    /// <summary>当前正在写入的文件代号</summary>
    public int CurrentFileId => _currentFileId;

    public LogFileWriter(string directory, string baseName, long maxBytes, int generations)
    {
        _directory = directory;
        _baseName = baseName;
        _maxBytes = maxBytes;
        _generations = generations;
    }

    /// <summary>
    /// 追加一段字节。写满上限会先滚动，因此调用方必须在<b>返回之后</b>才读
    /// <see cref="CurrentFileId"/>
    /// </summary>
    /// <param name="bytes">要写入的字节</param>
    /// <returns>这段字节在当前文件里的起始偏移</returns>
    public long Append(ReadOnlySpan<byte> bytes)
    {
        EnsureOpen();
        // 空文件即便超限也不滚动:否则一条超大正文会滚出一个又一个空文件
        if (_offset > 0 && _offset + bytes.Length > _maxBytes) Rotate();

        long start = _offset;
        _stream!.Write(bytes);
        _offset += bytes.Length;
        return start;
    }

    /// <summary>把托管缓冲推给操作系统。不是 fsync，代价是一次系统调用</summary>
    public void Flush() => _stream?.Flush();

    /// <summary>
    /// 文件代号对应的路径
    /// </summary>
    /// <param name="fileId">索引项里记的文件代号</param>
    /// <returns>路径；已被滚动淘汰（死链）时为 null</returns>
    public string? ResolvePath(int fileId)
    {
        int generation = _currentFileId - fileId;
        if (generation < 0 || generation >= _generations) return null;
        return GenerationPath(generation);
    }

    /// <summary>
    /// 读回一段正文
    /// </summary>
    /// <param name="fileId">文件代号</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="byteLength">字节长度</param>
    /// <returns>正文；死链或读失败时为 null</returns>
    public string? Read(int fileId, long offset, int byteLength)
    {
        string? path = ResolvePath(fileId);
        if (path == null || !File.Exists(path)) return null;

        // 要读的可能还在缓冲里没落盘,先推一次再读
        if (fileId == _currentFileId) Flush();

        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[byteLength];
            int read = fs.ReadAtLeast(buffer, byteLength, throwOnEndOfStream: false);
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception)
        {
            return null; //读日志失败不该影响任何事
        }
    }

    /// <summary>清空当前文件，并把代号推进一代（旧索引项随之变成死链）</summary>
    public void Reset()
    {
        Close();
        _currentFileId++;
        _offset = 0;
        File.Delete(GenerationPath(0));
    }

    /// <summary>
    /// 进程启动时滚动一次，让本次运行从一份干净的文件开始
    /// </summary>
    public void RotateOnStartup()
    {
        if (!File.Exists(GenerationPath(0))) return;
        Rotate();
    }

    private void EnsureOpen()
    {
        if (_stream != null) return;
        Directory.CreateDirectory(_directory);
        FileStream fs = new(GenerationPath(0), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _offset = fs.Length;
        _stream = new BufferedStream(fs, 64 * 1024);
    }

    // 从最老的一代开始依次往后挪,腾出第 0 代
    private void Rotate()
    {
        Close();

        string oldest = GenerationPath(_generations - 1);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int generation = _generations - 2; generation >= 0; generation--)
        {
            string from = GenerationPath(generation);
            if (File.Exists(from)) File.Move(from, GenerationPath(generation + 1), true);
        }

        _currentFileId++;
        _offset = 0;
        EnsureOpen();
    }

    private string GenerationPath(int generation) => Path.Combine(_directory,
        generation == 0 ? $"{_baseName}.txt" : $"{_baseName}.{generation}.txt");

    private void Close()
    {
        _stream?.Flush();
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Close();
}
