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
/// （read=0）」。这里在 <c>Flush</c>/<c>Read</c> 时校验文件头戳，发现被挪走就找到自己那份
/// 文件现在在第几代，<b>就在那一代接着写</b>，旧索引全部可读回。
///
/// ⚠ 被挪走的一方<b>不抢回 Log.txt</b>：从前这里会滚动一代抢回来，两个实例同时开着就你抢我、
/// 我抢你，每抢一次删掉最老的一代——实测 8 秒里两个实例互抢二十来次，十代日志全被冲成空文件。
/// 代价是被挪走期间不按大小滚动（滚动要动 Log.txt），那份文件可以超过上限。
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
    private int _generation; //正在写的那份文件在第几代:平时是 0,被别的实例挪走后是它现在的位置

    /// <summary>当前正在写入的文件代号</summary>
    public int CurrentFileId => _currentFileId;

    /// <summary>本实例的会话戳，盖在它写的每份文件头部</summary>
    public string SessionId { get; }

    public LogFileWriter(string directory, string baseName, long maxBytes, int generations)
    {
        _directory = directory;
        _baseName = baseName;
        _maxBytes = maxBytes;
        _generations = generations;
        SessionId = Guid.NewGuid().ToString("N")[..8];
        _sessionTag = LogFormat.SessionHeader(SessionId);
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
        // 空文件即便超限也不滚动:否则一条超大正文会滚出一个又一个空文件;被挪走期间也不滚动(见类注释)
        if (_generation == 0 && _offset > 0 && _offset + bytes.Length > _maxBytes) Rotate();

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
        int generation = _generation + _currentFileId - fileId;
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
        File.Delete(GenerationPath(_generation));
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
    /// 校验正在写的那份文件是否还在原位。被别的实例往后推了几代，就把 <c>_generation</c> 跟过去、
    /// 在那里接着写；文件号不变，<c>ResolvePath</c> 按新位置换算，已写日志全部可读回
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
            Close(); //旧句柄指向已被改名的文件,数据仍在原地
            int moved = FindCurrentFileGeneration();
            if (moved < 0)
            {
                // 自己那份已被推出链外:只能新开一份。这是唯一还会动 Log.txt 的情形,链要被推满十代才走到这里
                _generation = 0;
                Rotate();
                return;
            }
            _generation = moved;
            EnsureOpen();
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
        string path = GenerationPath(_generation);
        // 戳要用只读流核:追加流读不了
        if (File.Exists(path) && new FileInfo(path).Length > 0 && !OwnsFile(path))
        {
            // 非空且不是本会话:别人的文件,推一代后接管(只在启动、Reset 时走到,那时本就该新开 Log.txt)
            _generation = 0;
            Rotate();
            return;
        }

        FileStream fs = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        if (fs.Length == 0)
        {
            // 新文件:盖本会话戳作为身份标记,正文偏移一律从戳之后算
            byte[] tag = Encoding.UTF8.GetBytes(_sessionTag);
            fs.Write(tag);
            _offset = tag.Length;
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
        _generation = 0;
        _offset = 0;
        EnsureOpen();
    }

    // 正在写的那份文件现在在第几代:链只会往后推,所以从原位往后找第一份带本会话戳的
    // (更深处带戳的是本实例更早滚出去的文件);找不到为 -1
    private int FindCurrentFileGeneration()
    {
        for (int generation = _generation + 1; generation < _generations; generation++)
        {
            string path = GenerationPath(generation);
            if (!File.Exists(path)) continue;
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (StartsWithSessionTag(fs)) return generation;
            }
            catch (Exception)
            {
                // 单代读失败跳过,不影响其余世代
            }
        }
        return -1;
    }

    private bool IsCurrentOwned()
    {
        string path = GenerationPath(_generation);
        if (!File.Exists(path)) return true; //不存在:稍后 EnsureOpen 会创建并盖戳
        return OwnsFile(path);
    }

    private bool OwnsFile(string path)
    {
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
