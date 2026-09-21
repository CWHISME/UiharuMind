using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死 Write/Edit 的每文件并发锁：<c>AllowConcurrentInvocation=true</c> 下同一轮消息里
/// 两个工具调用并发执行，"读→计划→写"关键区不串行就会 lost-update（后写覆盖前写、静默丢改动）。
/// 锁表是进程级静态的，主代理×子代理各自实例也互斥（见 PermissiveFileAccessTools 字段注释）。
/// </summary>
public class PermissiveFileAccessToolsConcurrencyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-conc-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public PermissiveFileAccessToolsConcurrencyTests()
    {
        // 备份注入临时目录:Write 的自动备份不污染真实全局缓存
        _tools = new PermissiveFileAccessTools(_dir, new FileBackupStore(Path.Combine(_dir, "backups")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    /// <summary>
    /// 两个 Edit 并发改同一文件不同块：若不串行，第二个 Edit 读到的是第一个写盘前的旧内容，
    /// 它自己的改动会丢（整文件覆盖式落盘）。加了锁后两个都成功，文件是两个改动的并集。
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_SameFile_BothChangesSurvive()
    {
        string path = Path.Combine(_dir, "target.cs");
        await File.WriteAllTextAsync(path, "alpha\nbeta\ngamma\ndelta\n", TestContext.Current.CancellationToken);

        Task<string> editA = _tools.Edit("target.cs",
        [
            new FileEdit { OldString = "alpha", NewString = "ALPHA" },
        ], TestContext.Current.CancellationToken);
        Task<string> editB = _tools.Edit("target.cs",
        [
            new FileEdit { OldString = "delta", NewString = "DELTA" },
        ], TestContext.Current.CancellationToken);

        string[] results = await Task.WhenAll(editA, editB);

        Assert.All(results, r => Assert.DoesNotContain("[Edit failed]", r));
        Assert.Equal("ALPHA\nbeta\ngamma\nDELTA\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 不同文件的并发编辑互不阻塞：锁是按路径分的，A 文件的锁不能拖慢 B 文件。
    /// （若锁粒度错成文件集/全局，两个并发就会串行，测试通过 Task.WhenAll 超时暴露）
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_DifferentFiles_DoNotBlockEachOther()
    {
        string pathA = Path.Combine(_dir, "a.txt");
        string pathB = Path.Combine(_dir, "b.txt");
        await File.WriteAllTextAsync(pathA, "old\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(pathB, "old\n", TestContext.Current.CancellationToken);

        Task<string> editA = _tools.Edit("a.txt", [new FileEdit { OldString = "old", NewString = "A" }], TestContext.Current.CancellationToken);
        Task<string> editB = _tools.Edit("b.txt", [new FileEdit { OldString = "old", NewString = "B" }], TestContext.Current.CancellationToken);

        // 两文件并发应在锁内立刻完成；给个宽裕超时防死锁挂住整个测试套件
        Task all = Task.WhenAll(editA, editB);
        Task completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(all, completed); // 5 秒内都没跑完 → 锁粒度可能过粗，测试目的就是钉死它能并行

        Assert.Equal("A\n", await File.ReadAllTextAsync(pathA, TestContext.Current.CancellationToken));
        Assert.Equal("B\n", await File.ReadAllTextAsync(pathB, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Edit 失败路径也必须释放锁，否则下一次编辑同一文件会永久等待。
    /// </summary>
    [Fact]
    public async Task Edit_Failure_ReleasesLockForTheNextCall()
    {
        string path = Path.Combine(_dir, "fail.txt");
        await File.WriteAllTextAsync(path, "content\n", TestContext.Current.CancellationToken);

        // 第一次 edit 故意失败（oldString 找不到）——锁必须在 finally 里释放
        string failed = await _tools.Edit("fail.txt",
            [new FileEdit { OldString = "does-not-exist", NewString = "x" }], TestContext.Current.CancellationToken);
        Assert.Contains("[Edit failed]", failed);

        // 若失败路径没释放锁，这第二次会永久等待
        string ok = await _tools.Edit("fail.txt",
            [new FileEdit { OldString = "content", NewString = "CONTENT" }], TestContext.Current.CancellationToken);
        Assert.DoesNotContain("[Edit failed]", ok);
        Assert.Equal("CONTENT\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 主代理与子代理各自 new 一个 <see cref="PermissiveFileAccessTools"/>，锁表是进程级静态的，
    /// 两个实例编辑同一绝对路径必须互斥——否则主×子并发就是这波锁要防的核心场景。
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_AcrossInstances_ShareTheStaticLock()
    {
        string path = Path.Combine(_dir, "shared.cs");
        await File.WriteAllTextAsync(path, "one\ntwo\nthree\n", TestContext.Current.CancellationToken);

        // 两个实例、不同 workingDirectory 值，但编辑同一个绝对路径文件
        PermissiveFileAccessTools other = new(Path.Combine(_dir, "other"));
        Task<string> editA = _tools.Edit(path, [new FileEdit { OldString = "one", NewString = "ONE" }], TestContext.Current.CancellationToken);
        Task<string> editB = other.Edit(path, [new FileEdit { OldString = "three", NewString = "THREE" }], TestContext.Current.CancellationToken);

        string[] results = await Task.WhenAll(editA, editB);
        Assert.All(results, r => Assert.DoesNotContain("[Edit failed]", r));
        Assert.Equal("ONE\ntwo\nTHREE\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// macOS/Windows 文件系统大小写不敏感：<c>a.txt</c> 与 <c>A.TXT</c> 是同一文件，
    /// 必须拿到同一把锁（OrdinalIgnoreCase key），否则并发编辑各自覆盖。
    /// Linux 默认大小写敏感、不适用，跳过。
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_DifferentCasePaths_ShareTheLock()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return; //Linux 大小写敏感,不适用

        string path = Path.Combine(_dir, "case.txt");
        await File.WriteAllTextAsync(path, "first\nsecond\n", TestContext.Current.CancellationToken);

        string upperPath = Path.Combine(_dir, "CASE.TXT");
        Task<string> editA = _tools.Edit(path, [new FileEdit { OldString = "first", NewString = "FIRST" }], TestContext.Current.CancellationToken);
        Task<string> editB = _tools.Edit(upperPath, [new FileEdit { OldString = "second", NewString = "SECOND" }], TestContext.Current.CancellationToken);

        string[] results = await Task.WhenAll(editA, editB);
        Assert.All(results, r => Assert.DoesNotContain("[Edit failed]", r));
        Assert.Equal("FIRST\nSECOND\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 原子写是 rename 覆盖链接<b>本体</b>,不跟随目标——编辑 symlink 路径必须先把目标
    /// 解析成真实路径再落盘,否则链接被替换成普通文件、目标纹丝不动。
    /// Windows 上创建符号链接需管理员/Developer Mode,CI 常无此权限,跳过。
    /// </summary>
    [Fact]
    public async Task Edit_OnSymlink_UpdatesTheTarget_NotTheLink()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return; //Windows 创建符号链接需提权

        string target = Path.Combine(_dir, "real.txt");
        string link = Path.Combine(_dir, "link.txt");
        await File.WriteAllTextAsync(target, "original\n", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(link, target);

        string result = await _tools.Edit(link, [new FileEdit { OldString = "original", NewString = "edited" }], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("[Edit failed]", result);
        Assert.True(File.Exists(link) && new FileInfo(link).LinkTarget != null,
            "symlink 应保持为链接,不应被替换成普通文件");
        Assert.Equal("edited\n", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal("edited\n", await File.ReadAllTextAsync(link, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 同一个真实文件经 /real 与 /link 两条路径并发编辑,必须拿到同一把锁(symlink 解析后的
    /// 真实路径做 key),否则互斥失效、各自覆盖。
    /// Windows 上创建符号链接需管理员/Developer Mode,CI 常无此权限,跳过。
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_SameFile_OneViaSymlink_ShareTheLock()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return; //Windows 创建符号链接需提权

        string target = Path.Combine(_dir, "shared-real.cs");
        string link = Path.Combine(_dir, "shared-link.cs");
        await File.WriteAllTextAsync(target, "one\ntwo\nthree\n", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(link, target);

        Task<string> editA = _tools.Edit(target, [new FileEdit { OldString = "one", NewString = "ONE" }], TestContext.Current.CancellationToken);
        Task<string> editB = _tools.Edit(link, [new FileEdit { OldString = "three", NewString = "THREE" }], TestContext.Current.CancellationToken);

        string[] results = await Task.WhenAll(editA, editB);
        Assert.All(results, r => Assert.DoesNotContain("[Edit failed]", r));
        Assert.Equal("ONE\ntwo\nTHREE\n", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 原子替换新建 inode,必须把原文件的可执行位搬到 temp 再换,否则 755 脚本被编辑后
    /// 变 644、执行位静默丢失。非 Unix 平台跳过。
    /// 断言整个 mode 而不只是 UserExecute——若实现只复制了执行位、丢了 group/other 位,
    /// 测试也必须红。
    /// </summary>
    [Fact]
    public async Task Edit_PreservesExecutableBit()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return;

        string path = Path.Combine(_dir, "script.sh");
        await File.WriteAllTextAsync(path, "echo hi\n", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); //0755

        string result = await _tools.Edit(path, [new FileEdit { OldString = "echo hi", NewString = "echo bye" }], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("[Edit failed]", result);
        UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        Assert.Equal(expected, File.GetUnixFileMode(path));
        Assert.Equal("echo bye\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Write 工具同样走 SaveAsync,exec bit 也必须保留(不只 Edit 有这坑)</summary>
    [Fact]
    public async Task Write_PreservesExecutableBit()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return;

        string path = Path.Combine(_dir, "run.sh");
        await File.WriteAllTextAsync(path, "old\n", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); //0700

        string result = await _tools.Write(path, "new\n", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Error", result);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(path));
        Assert.Equal("new\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
}