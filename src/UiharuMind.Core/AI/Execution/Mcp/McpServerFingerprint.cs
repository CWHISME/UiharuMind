/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 一条 server 配置<b>可执行面</b>的指纹：传输方式、命令、参数、环境变量、地址、请求头。
///
/// <b>一份指纹，三个用途</b>，而且这三者必须是同一个判据：
/// <list type="number">
/// <item>安全确认的记忆键——用户授权的是「这一条命令」，不是「这个名字」；</item>
/// <item>连接失效判据——命令改了，旧连接连着的还是按旧命令起的那个子进程，必须换掉；</item>
/// <item>装配快照的入账内容——配置改了，装配要重建。</item>
/// </list>
/// 合一的收益是硬的：指纹变了 ⇒ 要重新确认 ⇒ 本来就必须先断掉旧连接，
/// 所以<b>不可能出现「授权还没给、进程已经按新命令起来了」</b>。
/// 若三者各用一套判据，这个缝隙迟早出现。
///
/// <b>不含名字</b>：名字是索引键而非可执行面，改名是另一件事（换了一条 server），
/// 由键本身区分，不该混进指纹。
/// </summary>
internal static class McpServerFingerprint
{
    /// 规范串里的分项分隔符。取控制字符是为了不可能出现在命令、参数或环境变量里——
    /// 用逗号之类的可打印字符,含该字符的参数就能拼出与另一组参数相同的规范串
    private const char Separator = '\u001f';

    /// <summary>
    /// 算一条配置的可执行面指纹。
    ///
    /// 字典按键排序后参与：<c>env</c> 与 <c>headers</c> 的书写顺序不是语义，
    /// 不排序的话在 json 里调换两行就会被当成「配置变了」而白白重新要一次授权。
    /// </summary>
    /// <param name="config">server 配置</param>
    /// <returns>十六进制指纹</returns>
    public static string Of(McpServerConfig config)
    {
        StringBuilder canonical = new();
        canonical.Append(config.TransportType).Append('\n');
        if (config.TransportType == EMcpTransportType.Http)
        {
            canonical.Append(config.Url).Append('\n');
            AppendMap(canonical, config.Headers);
        }
        else
        {
            canonical.Append(config.Command).Append('\n');
            // 参数逐项带分隔符:["a b"] 与 ["a","b"] 是两条不同的命令,拼成一行会撞成同一个指纹
            foreach (string arg in config.Args) canonical.Append(arg).Append(Separator);
            canonical.Append('\n');
            AppendMap(canonical, config.EnvironmentVariables);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    private static void AppendMap(StringBuilder canonical, IReadOnlyDictionary<string, string> map)
    {
        foreach ((string key, string value) in map.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            canonical.Append(key).Append('=').Append(value).Append(Separator);
        }

        canonical.Append('\n');
    }
}
