using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 明细导出。走<b>流式</b>写出：几万行也不进内存；排序与筛选与界面完全同一份 SQL 条件。
///
/// <para>
/// <b>另开一条只读连接</b>：导出要扫全量并写文件，占用单写连接的那把锁会把界面查询与落盘
/// 一起堵住几秒到几十秒。WAL 下读不阻塞写，代价只是导出期间看不到最新几行。
/// </para>
/// </summary>
public sealed partial class UsageStore
{
    /// <summary>CSV 表头（与 Excel 列顺序一致，中文表头）</summary>
    private const string CsvHeader =
        "时间,会话,模型,供应商,是否本地,输入token,缓存命中,缓存写入,输出token,思考token," +
        "总延迟ms,首token延迟ms,成功,错误类别,错误详情,状态码,重试次数,估算费用USD";

    /// <summary>CSV 用的固定时区格式：本地时间，秒级，Excel 直接认</summary>
    private const string CsvTimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// 把当前筛选的完整结果集写成 CSV（带 BOM）。
    /// <b>不动分页参数</b>：导出的就是「当前筛选下的全部」，不是当前页
    /// </summary>
    /// <param name="query">筛选与排序</param>
    /// <param name="path">目标文件；目录不存在会自动建</param>
    /// <returns>写出的数据行数</returns>
    public long ExportCsv(UsageDetailQuery query, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using SqliteConnection connection =
            new($"Data Source={_databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        string where = BuildDetailWhere(query, command);
        command.CommandText = $"""
                               SELECT ts, session_id, model_name, provider_key, is_local,
                                      input_tokens, cached_read, cached_write, output_tokens, reasoning_tokens,
                                      latency_ms, first_token_ms, ok, error_kind, error_detail, status_code,
                                      retry_count, cost_usd
                               FROM usage_record
                               WHERE {where}
                               ORDER BY {SortExpression(query.SortColumn)} {SortDirection(query.SortDirection)},
                                        id {SortDirection(query.SortDirection)};
                               """;

        //BOM 必须有:没有它 Excel 按 ANSI 解码,中文全是乱码
        using StreamWriter writer = new(path, false, new UTF8Encoding(true));
        writer.WriteLine(CsvHeader);
        long count = 0;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            writer.WriteLine(string.Join(',',
                FormatTime(reader.GetInt64(0)),
                Escape(reader.GetString(1)),
                Escape(reader.GetString(2)),
                Escape(reader.GetString(3)),
                reader.GetInt64(4) != 0 ? "是" : "否",
                reader.GetInt64(5).ToString(CultureInfo.InvariantCulture),
                reader.GetInt64(6).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(7) ? "" : reader.GetInt64(7).ToString(CultureInfo.InvariantCulture),
                reader.GetInt64(8).ToString(CultureInfo.InvariantCulture),
                reader.GetInt64(9).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(10) ? "" : reader.GetInt64(10).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(11) ? "" : reader.GetInt64(11).ToString(CultureInfo.InvariantCulture),
                reader.GetInt64(12) != 0 ? "是" : "否",
                reader.IsDBNull(13) ? "" : Escape(reader.GetString(13)),
                reader.IsDBNull(14) ? "" : Escape(reader.GetString(14)),
                reader.IsDBNull(15) ? "" : reader.GetInt64(15).ToString(CultureInfo.InvariantCulture),
                reader.GetInt64(16).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(17)
                    ? ""
                    : reader.GetDouble(17).ToString("0.######", CultureInfo.InvariantCulture)));
            count++;
        }

        writer.Flush();
        return count;
    }

    private static string FormatTime(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString(CsvTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// CSV 单元格转义：含逗号、引号、换行的字段要包引号（错误详情里这三种都可能出现）；
    /// 以 <c>= + - @</c> 或制表/回车开头的字段前置一个单引号——<b>模型名与错误详情都是可控输入</b>，
    /// 不挡的话 Excel 会把它们当公式执行（CSV 注入）
    /// </summary>
    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }
}
