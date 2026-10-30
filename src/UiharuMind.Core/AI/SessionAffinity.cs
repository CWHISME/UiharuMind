using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.AI;

/// <summary>
/// 会话亲和值的<b>唯一算法</b>：会话标识 → 一个能安全写进请求头、且与会话一一对应的短串。
///
/// 哈希而不是直接发会话 id：输出恒为 32 个十六进制字符，「能不能当请求头值」从运行时校验
/// 变成构造保证；且会话 id 同时是磁盘目录名，哈希只保留相等性、不保留可反查性。
/// 代价只在排查侧：日志里的值不能直接拿去对目录名，要对得先按这里算一遍。
///
/// 头名由供应商自己声明（<see cref="Models.ILlmModel.SessionAffinityHeaders"/>），不在这里——
/// 各家认的头不一样，那是供应商的知识，不该进公共层。
///
/// <b>什么时候需要它，取决于「缓存存在哪」，不取决于协议。</b>官方直连（Anthropic / OpenAI）的
/// 前缀缓存在服务端按内容哈希寻址、按组织（Anthropic 还按工作区）与模型隔离，没有「落在哪台副本」
/// 这回事——在头上发什么都不改变命中，OpenAI 那条路的路由提示是 body 里的 <c>prompt_cache_key</c>。
/// 而多副本的推理网关（自建 vLLM / SGLang、聚合网关）把 KV 缓存在<b>具体副本的显存</b>里，
/// 请求落到别台就是整段重算，这才需要把同一会话钉住。所以声明头名的只有聚合网关。
/// 反过来，「服务端已经做了缓存」推不出「不需要亲和」：前者只说缓存存在，后者问的是这一发能不能走到它跟前。
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
