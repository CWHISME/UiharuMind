namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 用量明细的读写契约。<b>UI 只认这一层</b>，不碰 SQL；聚合一律下沉到实现里，
/// 绝不把原表拉进内存再算（分页与几十万行不卡的前提）
/// </summary>
public interface IUsageStore
{
    /// <summary>
    /// 写一条请求级用量。<b>非阻塞</b>：实现应投进后台单写通道，绝不在调用线程上同步写盘
    /// （丢一条统计可以接受，卡住一次对话不可接受）
    /// </summary>
    /// <param name="record">一次请求的终态</param>
    void Append(UsageRecord record);

    /// <summary>
    /// 概览查询：KPI 分组行 + 趋势 + 失败原因分布，一次给出
    /// </summary>
    /// <param name="query">时间范围与筛选</param>
    /// <returns>聚合结果</returns>
    UsageSnapshot Query(UsageQuery query);

    /// <summary>
    /// 明细分页。<c>LIMIT/OFFSET</c> + 白名单排序列
    /// </summary>
    /// <param name="query">筛选、排序、分页</param>
    /// <returns>这一页与总行数</returns>
    UsageDetailPage QueryDetails(UsageDetailQuery query);

    /// <summary>
    /// 会话时间线（命中率视图的数据源）：按会话取请求明细，前端逐条算 <c>cached_read / input</c>
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="fromTs">起点（含）</param>
    /// <param name="toTs">终点（不含）</param>
    /// <returns>按时间升序的明细行</returns>
    IReadOnlyList<UsageDetailRow> QuerySessionTimeline(string sessionId, long fromTs, long toTs);

    /// <summary>
    /// 最近有记录的会话（筛选栏下拉），按最后活动时间倒序
    /// </summary>
    /// <param name="fromTs">起点（含）</param>
    /// <param name="toTs">终点（不含）</param>
    /// <param name="limit">最多取几个</param>
    /// <returns>会话汇总</returns>
    IReadOnlyList<SessionUsageSummary> ListSessions(long fromTs, long toTs, int limit = 100);

    /// <summary>
    /// 把当前筛选的<b>完整结果集</b>（不只是当前页）流式写成 CSV。带 BOM，Excel 打开中文不乱码
    /// </summary>
    /// <param name="query">筛选与排序（分页参数被忽略）</param>
    /// <param name="path">目标文件路径</param>
    /// <returns>写出的行数</returns>
    long ExportCsv(UsageDetailQuery query, string path);

    /// <summary>
    /// 范围内出现过的模型名（筛选栏下拉）
    /// </summary>
    /// <param name="fromTs">起点（含）</param>
    /// <param name="toTs">终点（不含）</param>
    /// <returns>模型名，按字典序</returns>
    IReadOnlyList<string> KnownModelNames(long fromTs, long toTs);

    /// <summary>
    /// 丢弃指定时刻之前的所有记录（保留策略；默认永久保留，不做 VACUUM）
    /// </summary>
    /// <param name="cutoffTs">这个时刻（不含）之前的记录会被删掉</param>
    /// <returns>删掉的行数</returns>
    int DeleteOlderThan(long cutoffTs);
}
