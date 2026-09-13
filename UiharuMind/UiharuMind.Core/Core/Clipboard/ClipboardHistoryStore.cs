/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Data.Sqlite;

namespace UiharuMind.Core.Core.Clipboard;

/// <summary>
/// 剪贴板历史的存储。<b>每次变更立即落盘</b>，一次变更就是一条 SQL，
/// 代价与历史总量无关——因此没有防抖、没有定时器，进程被强杀也不丢。
///
/// <para>
/// 旧实现是「整份 JSON 全量重写 + 每小时存一次 + 退出时存一次」：调试模式直接终止进程时
/// <c>Dispose</c> 不跑，丢的就是上次整点之后的全部记录。选 SQLite 而不是纯文本的理由
/// （与日志刚好相反）见 <c>docs/adr/0024</c>。
/// </para>
/// </summary>
public sealed class ClipboardHistoryStore : IDisposable
{
    /// <summary>预览的最大字符数，与日志那边同理：它是要常驻内存的那部分</summary>
    public const int PreviewLength = 400;

    private readonly SqliteConnection _connection;
    private readonly object _locker = new();
    private long _nextSortKey;

    public ClipboardHistoryStore(string databasePath)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;"); //崩溃一致性交给 WAL,不自己做
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("""
                CREATE TABLE IF NOT EXISTS history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at INTEGER NOT NULL,
                    text TEXT NOT NULL DEFAULT '',
                    preview TEXT NOT NULL DEFAULT '',
                    image_path TEXT,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    sort_key INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_history_sort ON history(sort_key DESC);
                """);

        _nextSortKey = ScalarLong("SELECT IFNULL(MAX(sort_key), 0) + 1 FROM history;");
    }

    /// <summary>
    /// 记一条文本
    /// </summary>
    /// <param name="text">正文，不截断</param>
    /// <returns>新条目</returns>
    public ClipboardHistoryEntry AddText(string text) => Add(text, null);

    /// <summary>
    /// 记一条图片
    /// </summary>
    /// <param name="imagePath">图片文件路径</param>
    /// <returns>新条目</returns>
    public ClipboardHistoryEntry AddImage(string imagePath) => Add(string.Empty, imagePath);

    /// <summary>
    /// 取一页历史，按排序键从大到小
    /// </summary>
    /// <param name="filter">筛选条件</param>
    /// <param name="beforeSortKey">上一页最后一条的排序键；首页传 null</param>
    /// <param name="limit">每页条数</param>
    /// <returns>这一页的条目；不足 <paramref name="limit"/> 条即表示没有下一页</returns>
    public List<ClipboardHistoryEntry> GetPage(ClipboardHistoryFilter filter, long? beforeSortKey, int limit)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = $"""
                                   SELECT id, created_at, preview, image_path, is_favorite, sort_key
                                   FROM history
                                   WHERE {BuildWhere(filter, beforeSortKey.HasValue)}
                                   ORDER BY sort_key DESC
                                   LIMIT @limit;
                                   """;
            BindFilter(command, filter);
            if (beforeSortKey.HasValue) command.Parameters.AddWithValue("@cursor", beforeSortKey.Value);
            command.Parameters.AddWithValue("@limit", limit);

            List<ClipboardHistoryEntry> result = [];
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadEntry(reader));
            return result;
        }
    }

    /// <summary>
    /// 按筛选条件统计条数
    /// </summary>
    /// <param name="filter">筛选条件</param>
    /// <returns>条数</returns>
    public int Count(ClipboardHistoryFilter filter)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM history WHERE {BuildWhere(filter, false)};";
            BindFilter(command, filter);
            return (int)(long)(command.ExecuteScalar() ?? 0L);
        }
    }

    /// <summary>
    /// 取一条的完整正文。列表只带预览，正文在点开或复制时才现取
    /// </summary>
    /// <param name="id">主键</param>
    /// <returns>正文；条目已不存在时为 null</returns>
    public string? GetText(long id)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT text FROM history WHERE id = @id;";
            command.Parameters.AddWithValue("@id", id);
            return command.ExecuteScalar() as string;
        }
    }

    /// <summary>
    /// 置顶。<b>只改排序键，不碰 created_at</b>——那是内容首次进剪贴板的时间，
    /// 复制一次就跳到现在是错的
    /// </summary>
    /// <param name="id">主键</param>
    /// <returns>新的排序键</returns>
    public long MoveToFront(long id)
    {
        lock (_locker)
        {
            long sortKey = _nextSortKey++;
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "UPDATE history SET sort_key = @sortKey WHERE id = @id;";
            command.Parameters.AddWithValue("@sortKey", sortKey);
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
            return sortKey;
        }
    }

    /// <summary>
    /// 改收藏标记
    /// </summary>
    /// <param name="id">主键</param>
    /// <param name="isFavorite">是否收藏</param>
    public void SetFavorite(long id, bool isFavorite)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "UPDATE history SET is_favorite = @favorite WHERE id = @id;";
            command.Parameters.AddWithValue("@favorite", isFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 删除若干条。
    /// <b>不删图片文件</b>，只把路径交回给调用方——文件必须在记录落盘之后再删，
    /// 反过来一旦崩在中间，历史里就留下一条指向不存在文件的记录，而那个没人能修
    /// </summary>
    /// <param name="ids">主键</param>
    /// <returns>被删条目里的图片路径，交由调用方删除文件</returns>
    public List<string> Delete(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return [];

        lock (_locker)
        {
            string placeholders = string.Join(',', ids.Select((_, i) => $"@id{i}"));
            List<string> images = [];

            using SqliteCommand select = _connection.CreateCommand();
            select.CommandText =
                $"SELECT image_path FROM history WHERE id IN ({placeholders}) AND image_path IS NOT NULL;";
            BindIds(select, ids);
            using (SqliteDataReader reader = select.ExecuteReader())
            {
                while (reader.Read()) images.Add(reader.GetString(0));
            }

            using SqliteCommand delete = _connection.CreateCommand();
            delete.CommandText = $"DELETE FROM history WHERE id IN ({placeholders});";
            BindIds(delete, ids);
            delete.ExecuteNonQuery();
            return images;
        }
    }

    /// <summary>
    /// 清空全部历史
    /// </summary>
    /// <returns>被删条目里的图片路径，交由调用方删除文件</returns>
    public List<string> Clear()
    {
        lock (_locker)
        {
            List<string> images = [];
            using (SqliteCommand select = _connection.CreateCommand())
            {
                select.CommandText = "SELECT image_path FROM history WHERE image_path IS NOT NULL;";
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read()) images.Add(reader.GetString(0));
            }

            Execute("DELETE FROM history;");
            return images;
        }
    }

    /// <summary>
    /// 清理指定时刻之前的记录。收藏项永远豁免
    /// </summary>
    /// <param name="deadline">这个时刻之前的记录会被清掉</param>
    /// <returns>被删条目里的图片路径，交由调用方删除文件</returns>
    public List<string> DeleteOlderThan(DateTime deadline)
    {
        lock (_locker)
        {
            long limit = ToUnixMilliseconds(deadline);
            List<string> images = [];
            using (SqliteCommand select = _connection.CreateCommand())
            {
                select.CommandText =
                    "SELECT image_path FROM history WHERE created_at < @limit AND is_favorite = 0 AND image_path IS NOT NULL;";
                select.Parameters.AddWithValue("@limit", limit);
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read()) images.Add(reader.GetString(0));
            }

            using SqliteCommand delete = _connection.CreateCommand();
            delete.CommandText = "DELETE FROM history WHERE created_at < @limit AND is_favorite = 0;";
            delete.Parameters.AddWithValue("@limit", limit);
            delete.ExecuteNonQuery();
            return images;
        }
    }

    /// <summary>
    /// 图片文件已存在、历史里却没有对应记录时补记一条（孤儿图片回收）
    /// </summary>
    /// <param name="imagePath">图片路径</param>
    /// <returns>已存在则为 null，补记了则为新条目</returns>
    public ClipboardHistoryEntry? AddImageIfMissing(string imagePath)
    {
        lock (_locker)
        {
            using SqliteCommand exists = _connection.CreateCommand();
            exists.CommandText = "SELECT 1 FROM history WHERE image_path = @path LIMIT 1;";
            exists.Parameters.AddWithValue("@path", imagePath);
            if (exists.ExecuteScalar() != null) return null;
        }

        return AddImage(imagePath);
    }

    private ClipboardHistoryEntry Add(string text, string? imagePath)
    {
        // 先按落盘精度(毫秒)归一,再往回转。不归一的话,刚插入时返回的 DateTime 带亚毫秒位,
        // 与随后从库里读回来的同一条不相等
        long createdAtMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        DateTime createdAt = FromUnixMilliseconds(createdAtMilliseconds);
        string preview = BuildPreview(text);

        lock (_locker)
        {
            long sortKey = _nextSortKey++;
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO history (created_at, text, preview, image_path, is_favorite, sort_key)
                                  VALUES (@createdAt, @text, @preview, @imagePath, 0, @sortKey);
                                  SELECT last_insert_rowid();
                                  """;
            command.Parameters.AddWithValue("@createdAt", createdAtMilliseconds);
            command.Parameters.AddWithValue("@text", text);
            command.Parameters.AddWithValue("@preview", preview);
            command.Parameters.AddWithValue("@imagePath", (object?)imagePath ?? DBNull.Value);
            command.Parameters.AddWithValue("@sortKey", sortKey);
            long id = (long)(command.ExecuteScalar() ?? 0L);
            return new ClipboardHistoryEntry(id, createdAt, preview, imagePath, false, sortKey);
        }
    }

    /// <summary>
    /// 取首行预览，与日志那边同一个口径：后面还有内容时补省略号
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>不含换行、长度受限的预览</returns>
    public static string BuildPreview(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        ReadOnlySpan<char> firstLine = end < 0 ? text : text.AsSpan(0, end);
        if (firstLine.Length <= PreviewLength) return end < 0 ? firstLine.ToString() : firstLine.ToString() + '…';
        return string.Concat(firstLine[..PreviewLength], "…");
    }

    private static string BuildWhere(ClipboardHistoryFilter filter, bool hasCursor)
    {
        List<string> conditions = ["1 = 1"];
        if (hasCursor) conditions.Add("sort_key < @cursor");
        if (filter.ImagesOnly) conditions.Add("image_path IS NOT NULL");
        if (filter.FavoritesOnly) conditions.Add("is_favorite = 1");
        if (!string.IsNullOrWhiteSpace(filter.Query)) conditions.Add("text LIKE @query ESCAPE '\\'");
        return string.Join(" AND ", conditions);
    }

    private static void BindFilter(SqliteCommand command, ClipboardHistoryFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Query)) return;
        // 关键字里的通配符要转义,否则用户搜 "100%" 会变成匹配任意内容
        string escaped = filter.Query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        command.Parameters.AddWithValue("@query", $"%{escaped}%");
    }

    private static void BindIds(SqliteCommand command, IReadOnlyCollection<long> ids)
    {
        int index = 0;
        foreach (long id in ids) command.Parameters.AddWithValue($"@id{index++}", id);
    }

    private static ClipboardHistoryEntry ReadEntry(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        FromUnixMilliseconds(reader.GetInt64(1)),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt64(4) != 0,
        reader.GetInt64(5));

    private static long ToUnixMilliseconds(DateTime value) => new DateTimeOffset(value).ToUnixTimeMilliseconds();

    private static DateTime FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime;

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long ScalarLong(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public void Dispose()
    {
        lock (_locker)
        {
            _connection.Close();
            _connection.Dispose();
        }
    }
}
