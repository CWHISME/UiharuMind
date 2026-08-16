using System.Text;
using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Extensions.VectorData;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 索引库里的一条记录。属性名即列名(见 <see cref="MemoryStore.CreateCollectionAsync"/>),
/// 改名或增删属性都会让既有索引对不上,必须同时抬 <see cref="MemoryData.CurrentIndexVersion"/>。
/// </summary>
internal sealed class MemoryChunkRecord
{
    public string Id { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = "";
    public ReadOnlyMemory<float> Embedding { get; set; }
}

/// <summary>
/// 记忆索引在磁盘上的布局与库文件生命周期。
///
/// 单独成类而不是留在 <see cref="MemoryData"/> 里：路径推导、原子替换、备份回滚是一组
/// 必须一起正确的操作，散在编排代码中间就会长出「换库成功但备份没删」「删了记忆库却留下
/// 孤儿 sqlite」这类半成功状态。所有碰 .sqlite 文件的代码都收在这里一处。
///
/// 库文件名跟随记忆库名称，所以改名必须连 side-car 一起搬——见 <see cref="MoveDatabaseFiles"/>。
/// </summary>
internal sealed class MemoryStore
{
    /// <summary>集合名。库里只有这一个集合，改它等于让所有既有索引失效</summary>
    private const string CollectionName = "chunks";

    private const string BackupSuffix = ".backup";
    private const string UpdatingSuffix = ".updating.sqlite";
    private const string DatabaseSuffix = ".sqlite";

    /// <summary>SQLite 的伴生文件后缀。删与搬都必须成套处理,漏一个就会读到半旧状态</summary>
    private static readonly string[] SideCarSuffixes = ["", "-wal", "-shm"];

    private readonly Func<string> _nameProvider; //取当前记忆库名。改名后路径要跟着变,所以不能在构造时定死

    /// <summary>
    /// 创建库文件管理器
    /// </summary>
    /// <param name="nameProvider">取当前记忆库名称的委托</param>
    public MemoryStore(Func<string> nameProvider)
    {
        _nameProvider = nameProvider;
    }

    /// <summary>正式库路径</summary>
    public string DatabasePath => GetDatabasePath(_nameProvider());

    /// <summary>索引构建期间的临时库路径。与正式库同目录,才能用 File.Replace 原子替换</summary>
    public string TemporaryDatabasePath =>
        Path.Combine(SettingConfig.MemoryEmbeddedPath, GetSafeFileName(_nameProvider()) + UpdatingSuffix);

    /// <summary>
    /// 按记忆库名推导正式库路径
    /// </summary>
    /// <param name="name">记忆库名称</param>
    /// <returns>库文件绝对路径</returns>
    public static string GetDatabasePath(string name)
    {
        return Path.Combine(SettingConfig.MemoryEmbeddedPath, GetSafeFileName(name) + DatabaseSuffix);
    }

    /// <summary>
    /// 打开(必要时创建)一个索引库集合
    /// </summary>
    /// <param name="databasePath">库文件路径</param>
    /// <param name="embeddingDimensions">向量维度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用的集合</returns>
    public static async Task<SqliteCollection<string, MemoryChunkRecord>> CreateCollectionAsync(
        string databasePath, int embeddingDimensions, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            {
                new VectorStoreKeyProperty(nameof(MemoryChunkRecord.Id), typeof(string)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.SourceName), typeof(string)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.SourceKind), typeof(string)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.SourceId), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.ChunkIndex), typeof(int)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.Text), typeof(string)),
                new VectorStoreVectorProperty(nameof(MemoryChunkRecord.Embedding), typeof(ReadOnlyMemory<float>),
                    embeddingDimensions)
                {
                    DistanceFunction = DistanceFunction.CosineDistance,
                    IndexKind = IndexKind.Flat
                }
            }
        };

        // 索引文件会被原子替换,关闭连接池可避免下次更新复用已被移动的旧文件句柄。
        var store = new SqliteVectorStore($"Data Source={databasePath};Pooling=False", null);
        SqliteCollection<string, MemoryChunkRecord> collection =
            store.GetCollection<string, MemoryChunkRecord>(CollectionName, definition);
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        return collection;
    }

    /// <summary>
    /// 用写好的临时库原子替换正式库
    /// </summary>
    public void ReplaceWithTemporary()
    {
        string databasePath = DatabasePath;
        string backupPath = databasePath + BackupSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        DeleteDatabaseFiles(backupPath);

        // 正式库和临时库位于同一目录,File.Replace 会以原子方式切换,并留下可回滚备份。
        if (File.Exists(databasePath))
        {
            File.Replace(TemporaryDatabasePath, databasePath, backupPath, true);
            try
            {
                DeleteDatabaseFiles(backupPath);
            }
            catch (Exception e)
            {
                // 新索引已经原子生效,备份清理失败只记录日志,不能把成功更新误报为失败。
                Log.Warning($"Memory index backup cleanup failed: {backupPath}, {e.Message}");
            }
        }
        else
        {
            File.Move(TemporaryDatabasePath, databasePath);
        }
    }

    /// <summary>
    /// 把正式库换成「空索引」:来源全部读失败或全为空时走这条,先移开再删,删不掉就搬回来
    /// </summary>
    public void ReplaceWithEmpty()
    {
        DeleteDatabaseFiles(TemporaryDatabasePath);

        string databasePath = DatabasePath;
        string backupPath = databasePath + BackupSuffix;
        DeleteDatabaseFiles(backupPath);
        if (!File.Exists(databasePath)) return;

        File.Move(databasePath, backupPath);
        try
        {
            DeleteDatabaseFiles(backupPath);
        }
        catch
        {
            File.Move(backupPath, databasePath, true);
            throw;
        }
    }

    /// <summary>丢弃写了一半的临时库</summary>
    public void DeleteTemporary() => DeleteDatabaseFiles(TemporaryDatabasePath);

    /// <summary>删掉这个记忆库的全部索引文件(正式库、临时库、备份)</summary>
    public void DeleteAll()
    {
        string databasePath = DatabasePath;
        DeleteDatabaseFiles(databasePath);
        DeleteDatabaseFiles(TemporaryDatabasePath);
        DeleteDatabaseFiles(databasePath + BackupSuffix);
    }

    /// <summary>
    /// 记忆库改名时把索引文件搬到新名字下。
    ///
    /// 改名走的是容器基类的「删条目 + 加条目」,它只认 json;索引库是 <see cref="MemoryData"/>
    /// 独有的 side-car,不搬就会变成孤儿,而新名字下没有库——检索静默返回空且不报错。
    /// </summary>
    /// <param name="oldName">原名称</param>
    /// <param name="newName">新名称(必须是容器去重后的最终名称)</param>
    /// <exception cref="IOException">目标已存在,或搬迁过程失败</exception>
    public static void MoveDatabaseFiles(string oldName, string newName)
    {
        string oldPath = GetDatabasePath(oldName);
        string newPath = GetDatabasePath(newName);
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return;

        // 顺带清掉两侧的临时库与备份:它们都以旧名字命名,留着只会让下次更新读到过期状态。
        DeleteDatabaseFiles(Path.Combine(
            SettingConfig.MemoryEmbeddedPath, GetSafeFileName(oldName) + UpdatingSuffix));
        DeleteDatabaseFiles(oldPath + BackupSuffix);
        if (!File.Exists(oldPath)) return; //还没建过索引,没什么可搬

        List<string> moved = [];
        try
        {
            foreach (string suffix in SideCarSuffixes)
            {
                string source = oldPath + suffix;
                if (!File.Exists(source)) continue;

                File.Move(source, newPath + suffix, false);
                moved.Add(suffix);
            }
        }
        catch
        {
            // 搬到一半失败:把已搬走的挪回去,让磁盘状态回到改名之前
            foreach (string suffix in moved)
            {
                try
                {
                    File.Move(newPath + suffix, oldPath + suffix, true);
                }
                catch (Exception e)
                {
                    Log.Error($"Memory index rename rollback failed: {newPath + suffix}, {e.Message}");
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 删除一个库文件及其伴生文件
    /// </summary>
    /// <param name="databasePath">库文件路径</param>
    public static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (string suffix in SideCarSuffixes)
        {
            string path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// 把记忆库名称转成可用的文件名。创建窗已挡下非法字符,这里是防御性兜底
    /// </summary>
    /// <param name="name">记忆库名称</param>
    /// <returns>安全文件名;全是非法字符时退回名称哈希前缀</returns>
    public static string GetSafeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = StringBuilderPool.Get();
        foreach (char character in name.Trim())
            builder.Append(invalidChars.Contains(character) ? '_' : character);

        string safeName = builder.ToString();
        StringBuilderPool.Release(builder);
        return string.IsNullOrWhiteSpace(safeName) ? MemorySourceId.FromValue(name)[..12] : safeName;
    }
}
