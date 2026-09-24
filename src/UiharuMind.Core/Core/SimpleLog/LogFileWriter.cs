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
///
/// 多实例防线：每个实例在自己文件的头部盖一个会话戳。别的实例 <c>RotateOnStartup</c>
/// 会把本实例的 <c>Log.txt</c> 改名并新开一个——本实例的写句柄还在往旧 inode 写（数据
/// 落在改名后的深处），而 <c>ResolvePath</c> 却指向新文件，表现为「详情面板空白
/// （read=0）」。这里在 <c>Flush</c>/<c>Read</c> 时校验文件头戳，发现被接管就按「我的
/// 最新数据所在的世代」校准 <c>_currentFileId</c> 并拿回 <c>Log.txt</c>，旧索引全部可读回。
/// </summary>
internal sealed class LogFileWriter : IDisposable
{
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _generations; //含当前代
    private readonly string _sessionTag; //本实例盖在文件头部的身份戳,如 "# session: 1234abcd\n"
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
        _sessionTag = $"# session: {Guid.NewGuid().ToString("N")[..8]}\n";
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

    /// <summary>把托管缓冲推给操作系统；顺带校验文件所有权，被别的实例接管时校准并拿回 Log.txt</summary>
    public void Flush()
    {
        EnsureOwnership();
        _stream?.Flush();
    }

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
        // 先校准:当前 Log.txt 可能已被别的实例接管,ResolvePath 必须在校准之后才算
        try
        {
            EnsureOwnership();
        }
        catch (Exception)
        {
            // 校准失败不阻塞读取:用未校准的代号继续尝试
        }

        // 要读的可能还在缓冲里没落盘,先推一次再读
        if (fileId == _currentFileId) Flush();

        string? path = ResolvePath(fileId);
        if (path == null || !File.Exists(path)) return null;

        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[byteLength];
            int read = fs.ReadAtLeast(buffer, byteLength, throwOnEndOfStream: false);
            // 读不满 = 文件被外部换新/截断(索引指向的是别人的文件),按死链处理,
            // 不返回截断的正文或空串——那正是「详情面板空白」的现场
            if (read < byteLength) return null;
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

    /// <summary>
    /// 校验 Log.txt 是否仍归本实例。被别的实例换走后：
    /// 按「我的最新数据所在世代」校准 <c>_currentFileId</c>，再 Rotate 拿回 Log.txt。
    /// 外部每推一代,<c>currentFileId</c> 与全目录文件同步 +1,校准后旧索引的
    /// <c>ResolvePath</c> 恰好指向改名后的文件,已写日志全部可读回。
    /// </summary>
    private void EnsureOwnership()
    {
        if (_stream == null)
        {
            EnsureOpen(); //首次打开(或 Reset 后):内部会盖本会话戳
            return;
        }

        // 先落盘再检测:刚写标记/正文的新文件在磁盘上可能还是空的,
        // 不 flush 会被误判为「被别的实例接管」而自滚动推一代
        _stream.Flush();

        if (IsCurrentOwned()) return;

        try
        {
            _currentFileId += FindLatestOwnedGeneration();
            Close(); //旧句柄指向已被改名的文件,数据仍在原地
            Rotate(); //全目录推一代并拿回 Log.txt(Rotate 内部 EnsureOpen 会盖新戳)
        }
        catch (Exception)
        {
            // 接管/校准失败不阻塞写入与读取,下次 Flush 再试
        }
    }

    private void EnsureOpen()
    {
        if (_stream != null) return;
        Directory.CreateDirectory(_directory);
        FileStream fs = new(GenerationPath(0), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        if (fs.Length == 0)
        {
            // 新文件:盖本会话戳作为身份标记,正文偏移一律从戳之后算
            byte[] tag = Encoding.UTF8.GetBytes(_sessionTag);
            fs.Write(tag);
            _offset = tag.Length;
        }
        else if (!StartsWithSessionTag(fs))
        {
            // 非空且不是本会话:别人的文件,推一代后接管
            fs.Dispose();
            Rotate();
            return;
        }
        else
        {
            _offset = fs.Length; //本会话文件,继续追加
        }
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

    // 本会话标记出现的最大世代号:外部实例每启动一次就把整条链推一代,
    // 「最大世代」正是本实例最后写入的那份文件被外部改名后的位置
    private int FindLatestOwnedGeneration()
    {
        int latest = 0;
        for (int generation = 1; generation < _generations; generation++)
        {
            string path = GenerationPath(generation);
            if (!File.Exists(path)) continue;
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (StartsWithSessionTag(fs)) latest = generation;
            }
            catch (Exception)
            {
                // 单代读失败跳过,不影响其余世代
            }
        }
        return latest;
    }

    private bool IsCurrentOwned()
    {
        string path = GenerationPath(0);
        if (!File.Exists(path)) return true; //Log.txt 不存在:稍后 EnsureOpen 会创建并盖戳
        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return StartsWithSessionTag(fs);
        }
        catch (Exception)
        {
            return true; //读失败不阻塞写入
        }
    }

    private bool StartsWithSessionTag(FileStream fs)
    {
        byte[] tag = Encoding.UTF8.GetBytes(_sessionTag);
        byte[] buffer = new byte[tag.Length];
        int read = fs.Read(buffer, 0, buffer.Length);
        return read == tag.Length && buffer.AsSpan().SequenceEqual(tag);
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
