using Microsoft.Data.Sqlite;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 用量明细的 SQLite 实现。写法照 <c>ClipboardHistoryStore</c>（ADR 0024）：WAL + 一条变更一条 SQL。
///
/// <para>
/// <b>线程模型</b>：一个连接 + 一把锁。写入由 <see cref="UsageJournal"/> 的单写线程串行调用，
/// 读取来自界面线程的分页/聚合查询；两者在这把锁上排队，代价是一次查询而不是整表重写。
/// </para>
/// <para>
/// <b>它刻意不实现 <see cref="IUsageStore"/></b>：这里每一发都是同步写盘，而契约要求
/// <c>Append</c> 非阻塞。门面是 <see cref="UsageJournal"/>，它把写入投进后台通道。<b>请求路径上
/// 永远不该直接拿到这个类</b>。
/// </para>
/// <para>
/// 表结构见设计文档 3.2：模型三维冗余进记录行、<c>cached_write</c>/<c>cost_usd</c> 允许 NULL
/// （「不知道」不是「零」）、<c>price_*</c> 四列留证据。
/// </para>
/// </summary>
public sealed partial class UsageStore : IDisposable
{
    /// <summary>会话时间线的硬上限：长会话单个区间内可能有几万次调用，不该整段进内存再上屏</summary>
    private const int TimelineLimit = 5000;

    private const string InsertSql = """
                                     INSERT INTO usage_record (
                                         ts, started_ts, session_id, model_name, model_id, provider_id, provider_key,
                                         api_base, is_local, input_tokens, cached_read, cached_write, output_tokens,
                                         reasoning_tokens, latency_ms, first_token_ms, ok, error_kind, error_detail,
                                         status_code, retry_count, cost_usd, price_source, price_input, price_output,
                                         price_cache_read, price_multiplier)
                                     VALUES (
                                         @ts, @startedTs, @sessionId, @modelName, @modelId, @providerId, @providerKey,
                                         @apiBase, @isLocal, @input, @cachedRead, @cachedWrite, @output,
                                         @reasoning, @latency, @firstToken, @ok, @errorKind, @errorDetail,
                                         @statusCode, @retryCount, @cost, @priceSource, @priceInput, @priceOutput,
                                         @priceCacheRead, @multiplier);
                                     """;

    private readonly SqliteConnection _connection;
    private readonly string _databasePath; //导出时另开一条只读连接要用
    private readonly object _locker = new();

    /// <summary>
    /// 打开（必要时创建）用量库
    /// </summary>
    /// <param name="databasePath">数据库文件路径；目录不存在会自动建</param>
    public UsageStore(string databasePath)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _databasePath = databasePath;
        _connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;"); //崩溃一致性交给 WAL，不自己做
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("""
                CREATE TABLE IF NOT EXISTS usage_record (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts               INTEGER NOT NULL,
                    started_ts       INTEGER NOT NULL,
                    session_id       TEXT    NOT NULL,
                    model_name       TEXT    NOT NULL,
                    model_id         TEXT    NOT NULL DEFAULT '',
                    provider_id      TEXT    NOT NULL DEFAULT '',
                    provider_key     TEXT    NOT NULL DEFAULT '',
                    api_base         TEXT    NOT NULL DEFAULT '',
                    is_local         INTEGER NOT NULL DEFAULT 0,
                    input_tokens     INTEGER NOT NULL DEFAULT 0,
                    cached_read      INTEGER NOT NULL DEFAULT 0,
                    cached_write     INTEGER,
                    output_tokens    INTEGER NOT NULL DEFAULT 0,
                    reasoning_tokens INTEGER NOT NULL DEFAULT 0,
                    latency_ms       INTEGER,
                    first_token_ms   INTEGER,
                    ok               INTEGER NOT NULL DEFAULT 1,
                    error_kind       TEXT,
                    error_detail     TEXT,
                    status_code      INTEGER,
                    retry_count      INTEGER NOT NULL DEFAULT 0,
                    cost_usd         REAL,
                    price_source     TEXT,
                    price_input      REAL,
                    price_output     REAL,
                    price_cache_read REAL,
                    price_multiplier REAL NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS idx_usage_ts        ON usage_record(ts);
                CREATE INDEX IF NOT EXISTS idx_usage_ts_model  ON usage_record(ts, model_name);
                CREATE INDEX IF NOT EXISTS idx_usage_ts_prov   ON usage_record(ts, provider_key);
                CREATE INDEX IF NOT EXISTS idx_usage_session   ON usage_record(session_id, ts);
                """);
    }

    /// <summary>
    /// 同步写一条记录。<b>只许后台单写线程调</b>（见类注释）
    /// </summary>
    /// <param name="record">一次请求的终态</param>
    public void Insert(UsageRecord record)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = InsertSql;
            command.Parameters.AddWithValue("@ts", record.Ts);
            command.Parameters.AddWithValue("@startedTs", record.StartedTs);
            command.Parameters.AddWithValue("@sessionId", record.SessionId);
            command.Parameters.AddWithValue("@modelName", record.ModelName);
            command.Parameters.AddWithValue("@modelId", record.ModelId);
            command.Parameters.AddWithValue("@providerId", record.ProviderId);
            command.Parameters.AddWithValue("@providerKey", record.ProviderKey);
            command.Parameters.AddWithValue("@apiBase", record.ApiBase);
            command.Parameters.AddWithValue("@isLocal", record.IsLocal ? 1 : 0);
            command.Parameters.AddWithValue("@input", record.InputTokens);
            command.Parameters.AddWithValue("@cachedRead", record.CachedRead);
            command.Parameters.AddWithValue("@cachedWrite", (object?)record.CachedWrite ?? DBNull.Value);
            command.Parameters.AddWithValue("@output", record.OutputTokens);
            command.Parameters.AddWithValue("@reasoning", record.ReasoningTokens);
            command.Parameters.AddWithValue("@latency", (object?)record.LatencyMs ?? DBNull.Value);
            command.Parameters.AddWithValue("@firstToken", (object?)record.FirstTokenMs ?? DBNull.Value);
            command.Parameters.AddWithValue("@ok", record.Ok ? 1 : 0);
            command.Parameters.AddWithValue("@errorKind", (object?)record.ErrorKind ?? DBNull.Value);
            command.Parameters.AddWithValue("@errorDetail", (object?)record.ErrorDetail ?? DBNull.Value);
            command.Parameters.AddWithValue("@statusCode", (object?)record.StatusCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@retryCount", record.RetryCount);
            command.Parameters.AddWithValue("@cost", (object?)ToDouble(record.CostUsd) ?? DBNull.Value);
            command.Parameters.AddWithValue("@priceSource", (object?)record.PriceSource ?? DBNull.Value);
            command.Parameters.AddWithValue("@priceInput", (object?)ToDouble(record.PriceInput) ?? DBNull.Value);
            command.Parameters.AddWithValue("@priceOutput", (object?)ToDouble(record.PriceOutput) ?? DBNull.Value);
            command.Parameters.AddWithValue("@priceCacheRead", (object?)ToDouble(record.PriceCacheRead) ?? DBNull.Value);
            command.Parameters.AddWithValue("@multiplier", (double)record.Multiplier);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>概览查询：KPI 分组行 + 趋势 + 失败原因分布</summary>
    /// <param name="query">时间范围与筛选</param>
    /// <returns>聚合结果</returns>
    public UsageSnapshot Query(UsageQuery query)
    {
        lock (_locker)
        {
            return new UsageSnapshot(
                QueryAggregates(query),
                QueryTrend(query),
                QueryFailureKinds(query));
        }
    }

    /// <summary>明细分页（<c>LIMIT/OFFSET</c> + 白名单排序）</summary>
    /// <param name="query">筛选、排序、分页</param>
    /// <returns>这一页与总行数</returns>
    public UsageDetailPage QueryDetails(UsageDetailQuery query)
    {
        lock (_locker)
        {
            using SqliteCommand countCommand = _connection.CreateCommand();
            string where = BuildDetailWhere(query, countCommand);
            countCommand.CommandText = $"SELECT COUNT(*) FROM usage_record WHERE {where};";
            int total = Convert.ToInt32(countCommand.ExecuteScalar() ?? 0);

            List<UsageDetailRow> rows = [];
            using SqliteCommand command = _connection.CreateCommand();
            string detailWhere = BuildDetailWhere(query, command);
            command.CommandText = $"""
                                   {DetailSelect}
                                   WHERE {detailWhere}
                                   ORDER BY {SortExpression(query.SortColumn)} {SortDirection(query.SortDirection)},
                                            id {SortDirection(query.SortDirection)}
                                   LIMIT @limit OFFSET @offset;
                                   """;
            command.Parameters.AddWithValue("@limit", Math.Clamp(query.Limit, 1, 1000));
            command.Parameters.AddWithValue("@offset", Math.Max(0, query.Offset));
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read()) rows.Add(ReadDetailRow(reader));
            }

            return new UsageDetailPage(rows, total);
        }
    }

    /// <summary>会话时间线：按会话取请求明细</summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="fromTs">起点（含）</param>
    /// <param name="toTs">终点（不含）</param>
    /// <param name="limit">最多取多少条（从最早的算起，防止长会话把整段拉进内存）</param>
    /// <returns>按时间升序的明细行</returns>
    public IReadOnlyList<UsageDetailRow> QuerySessionTimeline(string sessionId, long fromTs, long toTs,
        int limit = TimelineLimit)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = $"""
                                   {DetailSelect}
                                   WHERE session_id = @sessionId AND ts >= @from AND ts < @to
                                   ORDER BY ts, id
                                   LIMIT @limit;
                                   """;
            command.Parameters.AddWithValue("@sessionId", sessionId);
            command.Parameters.AddWithValue("@from", fromTs);
            command.Parameters.AddWithValue("@to", toTs);
            command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, TimelineLimit));
            List<UsageDetailRow> rows = [];
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadDetailRow(reader));
            return rows;
        }
    }

    /// <summary>最近有记录的会话</summary>
    /// <param name="fromTs">起点（含）</param>
    /// <param name="toTs">终点（不含）</param>
    /// <param name="limit">最多取几个</param>
    /// <returns>会话汇总</returns>
    public IReadOnlyList<SessionUsageSummary> ListSessions(long fromTs, long toTs, int limit = 100)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                                  SELECT session_id, COUNT(*), MIN(ts), MAX(ts), SUM(cost_usd)
                                  FROM usage_record
                                  WHERE ts >= @from AND ts < @to
                                  GROUP BY session_id
                                  ORDER BY MAX(ts) DESC
                                  LIMIT @limit;
                                  """;
            command.Parameters.AddWithValue("@from", fromTs);
            command.Parameters.AddWithValue("@to", toTs);
            command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));
            List<SessionUsageSummary> rows = [];
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new SessionUsageSummary(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    ReadNullableDecimal(reader, 4)));
            }

            return rows;
        }
    }

    /// <summary>范围内出现过的模型名</summary>
    /// <param name="fromTs">起点（含）</param>
    /// <param name="toTs">终点（不含）</param>
    /// <returns>模型名，按字典序</returns>
    public IReadOnlyList<string> KnownModelNames(long fromTs, long toTs)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                                  SELECT DISTINCT model_name FROM usage_record
                                  WHERE ts >= @from AND ts < @to
                                  ORDER BY model_name;
                                  """;
            command.Parameters.AddWithValue("@from", fromTs);
            command.Parameters.AddWithValue("@to", toTs);
            List<string> names = [];
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }
    }

    /// <summary>丢弃指定时刻之前的记录（不做 VACUUM）</summary>
    /// <param name="cutoffTs">这个时刻（不含）之前的记录会被删掉</param>
    /// <returns>删掉的行数</returns>
    public int DeleteOlderThan(long cutoffTs)
    {
        lock (_locker)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM usage_record WHERE ts < @cutoff;";
            command.Parameters.AddWithValue("@cutoff", cutoffTs);
            return command.ExecuteNonQuery();
        }
    }

    private List<UsageAggregateRow> QueryAggregates(UsageQuery query)
    {
        using SqliteCommand command = _connection.CreateCommand();
        string where = BuildQueryWhere(query, command);
        command.CommandText = $"""
                               SELECT model_name, provider_key, is_local,
                                      SUM(input_tokens), SUM(cached_read), SUM(output_tokens),
                                      SUM(reasoning_tokens), COUNT(*),
                                      SUM(CASE WHEN ok = 0 THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN ok = 0 AND IFNULL(error_kind, '') <> '{UsageErrorKinds.Cancel}'
                                               THEN 1 ELSE 0 END),
                                      SUM(retry_count), SUM(cost_usd),
                                      SUM(CASE WHEN price_cache_read IS NOT NULL AND price_input IS NOT NULL
                                               THEN cached_read * (price_input - price_cache_read) END) / 1000000.0,
                                      SUM(CASE WHEN ok = 1 THEN latency_ms END),
                                      SUM(CASE WHEN ok = 1 THEN first_token_ms END),
                                      SUM(CASE WHEN ok = 1 THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN cached_write IS NOT NULL THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN price_source = '{UsagePricing.SourceOfficial}' THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN price_source = '{UsagePricing.SourceModelsDev}' THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN ok = 1 AND cost_usd IS NULL AND is_local = 0 THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN is_local = 1 THEN 1 ELSE 0 END)
                               FROM usage_record
                               WHERE {where}
                               GROUP BY model_name, provider_key, is_local
                               ORDER BY SUM(input_tokens + output_tokens) DESC;
                               """;

        List<UsageAggregateRow> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new UsageAggregateRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(16),
                reader.GetInt32(17),
                reader.GetInt32(18),
                reader.GetInt32(19),
                reader.GetInt32(20),
                ReadNullableDecimal(reader, 11),
                ReadNullableDecimal(reader, 12),
                reader.IsDBNull(13) ? 0 : reader.GetInt64(13),
                reader.IsDBNull(14) ? 0 : reader.GetInt64(14),
                reader.GetInt32(15)));
        }

        return rows;
    }

    private List<UsageTrendPoint> QueryTrend(UsageQuery query)
    {
        long bucketMs = UsageBucket.SizeOf(query.Range.Granularity);
        long anchor = UsageBucket.AnchorFor(query.Range, TimeZoneInfo.Local);
        using SqliteCommand command = _connection.CreateCommand();
        string where = BuildQueryWhere(query, command);
        command.CommandText = $"""
                               SELECT ((ts - @anchor) / @bucketMs) * @bucketMs + @anchor AS bucket,
                                      SUM(input_tokens + output_tokens),
                                      SUM(cost_usd), COUNT(*),
                                      SUM(CASE WHEN ok = 1 THEN latency_ms END),
                                      SUM(CASE WHEN ok = 1 THEN 1 ELSE 0 END),
                                      SUM(CASE WHEN ok = 0 AND IFNULL(error_kind, '') <> '{UsageErrorKinds.Cancel}'
                                               THEN 1 ELSE 0 END)
                               FROM usage_record
                               WHERE {where}
                               GROUP BY bucket ORDER BY bucket;
                               """;
        command.Parameters.AddWithValue("@anchor", anchor);
        command.Parameters.AddWithValue("@bucketMs", bucketMs);

        List<UsageTrendPoint> points = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new UsageTrendPoint(
                reader.GetInt64(0),
                reader.GetInt64(1),
                ReadNullableDecimal(reader, 2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        return points;
    }

    private List<FailureKindStat> QueryFailureKinds(UsageQuery query)
    {
        using SqliteCommand command = _connection.CreateCommand();
        string where = BuildQueryWhere(query, command);
        command.CommandText = $"""
                               SELECT error_kind, COUNT(*), AVG(retry_count),
                                      SUM(CASE WHEN status_code IS NOT NULL THEN 1 ELSE 0 END)
                               FROM usage_record
                               WHERE {where} AND ok = 0
                               GROUP BY error_kind ORDER BY COUNT(*) DESC;
                               """;

        List<FailureKindStat> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new FailureKindStat(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetInt32(3)));
        }

        return rows;
    }

    private const string DetailSelect = """
                                        SELECT id, ts, started_ts, session_id, model_name, provider_key, is_local,
                                               input_tokens, cached_read, cached_write, output_tokens, reasoning_tokens,
                                               latency_ms, first_token_ms, ok, error_kind, error_detail, status_code,
                                               retry_count, cost_usd
                                        FROM usage_record
                                        """;

    private static UsageDetailRow ReadDetailRow(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetInt64(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt64(6) != 0,
        reader.GetInt64(7),
        reader.GetInt64(8),
        reader.IsDBNull(9) ? null : reader.GetInt64(9),
        reader.GetInt64(10),
        reader.GetInt64(11),
        reader.IsDBNull(12) ? null : reader.GetInt32(12),
        reader.IsDBNull(13) ? null : reader.GetInt32(13),
        reader.GetInt64(14) != 0,
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : reader.GetString(16),
        reader.IsDBNull(17) ? null : reader.GetInt32(17),
        reader.GetInt32(18),
        ReadNullableDecimal(reader, 19));

    /// <summary>筛选条件只这一处拼。模型/会话都是参数，<c>onlyFailed</c> 是布尔开关，用户输入永远不拼进 SQL</summary>
    private static string BuildWhere(UsageRange range, string? modelFilter, string? sessionFilter, bool onlyFailed,
        SqliteCommand command)
    {
        command.Parameters.AddWithValue("@from", range.FromTs);
        command.Parameters.AddWithValue("@to", range.ToTs);
        List<string> conditions = ["ts >= @from", "ts < @to"];
        if (!string.IsNullOrEmpty(modelFilter))
        {
            conditions.Add("model_name = @model");
            command.Parameters.AddWithValue("@model", modelFilter);
        }

        if (!string.IsNullOrEmpty(sessionFilter))
        {
            conditions.Add("session_id = @session");
            command.Parameters.AddWithValue("@session", sessionFilter);
        }

        if (onlyFailed) conditions.Add("ok = 0");
        return string.Join(" AND ", conditions);
    }

    private static string BuildQueryWhere(UsageQuery query, SqliteCommand command) =>
        BuildWhere(query.Range, query.ModelFilter, query.SessionFilter, false, command);

    private static string BuildDetailWhere(UsageDetailQuery query, SqliteCommand command) =>
        BuildWhere(query.Range, query.ModelFilter, query.SessionFilter, query.OnlyFailed, command);

    /// <summary>排序列白名单：用户输入永远不拼进 SQL</summary>
    private static string SortExpression(EUsageDetailSortColumn column) => column switch
    {
        EUsageDetailSortColumn.Model => "model_name",
        EUsageDetailSortColumn.InputTokens => "input_tokens",
        EUsageDetailSortColumn.OutputTokens => "output_tokens",
        EUsageDetailSortColumn.CachedRead => "cached_read",
        EUsageDetailSortColumn.Latency => "latency_ms",
        EUsageDetailSortColumn.Cost => "cost_usd",
        _ => "ts",
    };

    private static string SortDirection(EUsageSortDirection direction) =>
        direction == EUsageSortDirection.Ascending ? "ASC" : "DESC";

    private static double? ToDouble(decimal? value) => value is { } v ? (double)v : null;

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : (decimal)reader.GetDouble(index);

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>关闭连接。进程退出时由 <see cref="UsageJournal"/> 调一次</summary>
    public void Dispose()
    {
        lock (_locker)
        {
            _connection.Dispose();
        }
    }
}
