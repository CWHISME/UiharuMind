using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.AI;

/// <summary>
/// 会话亲和值的<b>唯一算法</b>：会话标识 → 一个能安全写进请求头、且与会话一一对应的短串。
///
/// <b>为什么要哈希而不是直接发会话 id</b>：
/// 1. 合法性变成构造保证。请求头值的字符集有限制，而会话标识是可反序列化、可跨进程传递的字段，
///    直接发就得在发请求那一层做校验、再不合法时放弃（放弃 = 少一次缓存命中）。哈希之后
///    输出永远是 32 个十六进制字符，校验分支从"处理数据"退化成"防调用方写错"。
/// 2. 不外泄标识。会话 id 同时是磁盘目录名，是一个稳定的关联符；发原始值等于告诉网关
///    "这几十个请求属于同一台机器上的同一个会话"。哈希只保留"相等性"，不保留可反查性。
/// 3. 长度与形态统一。不管标识长什么样，网线上永远是同形的 32 字符。
///
/// 网关只要求"同一会话的请求带同一个值"，不要求这个值能读出来是什么——所以不可逆没有代价，
/// 代价在排查侧：日志里看到的值不能直接拿去对目录名，要对得先按这里算一遍。
///
/// <b>必须是唯一算法</b>：<see cref="Chat.ChatSession.CacheAffinity"/> 与
/// <see cref="Execution.Assembly.AgentBuildProfile.CacheAffinity"/> 各自从会话标识推导一次，
/// 两处若算出不同的值，同一个会话的主代理请求与旁路请求就会落到不同副本上——
/// 那比不带亲和更糟（两套前缀各钉一台机器，谁也不命中）。所以谁都不许自己拼。
/// </summary>
public static class SessionAffinity
{
    /// <summary>输出的十六进制字符数：32 个 = 128 位，与一个 GUID 同形，撞车概率可忽略</summary>
    private const int Length = 32;

    /// <summary>
    /// 由会话标识算出亲和值。<b>确定性</b>：同一个标识在任何进程、任何版本、任何平台都得到同一个值，
    /// 跨进程共享同一份档案（见 <c>SessionManager.CrossProcess</c>）时两边才落在同一后端。
    /// </summary>
    /// <param name="sessionId">会话标识，非空</param>
    /// <returns>32 个小写十六进制字符</returns>
    public static string Of(string sessionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexStringLower(hash.AsSpan(0, Length / 2));
    }
}
