namespace UiharuMind.Core.AI.Net.Response;

/// <summary>
/// 一条响应流里「思考」相关字段的普查：哪些键出现过、各几次。
///
/// 存在的理由只有一个——<b>加密思考有没有下发，目前没有任何观测手段</b>。明文思考会在界面上出现，
/// 看不见就等于没发；而 <c>reasoning_details</c> 里 <c>reasoning.encrypted</c> 那种 blob 不进界面，
/// 下游的 MEAI 适配器又只认 <c>reasoning_content</c>（其它键在适配层就被丢掉），
/// 于是「服务端到底给没给」在客户端两侧都不可见。muse-spark 正是这种：usage 报上千个思考 token，
/// 正文与思考一个字都没有——是「它没发」，还是「发了我们丢了」，在此之前只能靠猜。
///
/// 每个 data 行扫一次（绝大多数行第一下就不命中），流结束时并进清洗流那一行日志。
/// 计数是<b>逐帧累加</b>的，一条流里同一字段出现多次就是多次——要的是「有没有、大概什么形态」，不是精确统计。
/// </summary>
internal sealed class SseReasoningCensus
{
    // 所有思考键都以它开头:先找这个再往后认,一次扫描覆盖全部
    private static readonly byte[] KeyPrefix = "\"reasoning"u8.ToArray();

    private static readonly byte[] ContentSuffix = "_content\""u8.ToArray();
    private static readonly byte[] DetailsSuffix = "_details\""u8.ToArray();
    private static readonly byte[] TextSuffix = "_text\""u8.ToArray();
    private static readonly byte[] BareSuffix = "\""u8.ToArray();
    private static readonly byte[] EncryptedDetailSuffix = ".encrypted\""u8.ToArray(); //reasoning.encrypted
    // Kimi 方言 {type:"encrypted",…} 不挂 reasoning. 前缀。认 type 而不是同名值键(一个条目会出现两次)
    private static readonly byte[] EncryptedEntryKey = "\"type\":\"encrypted\""u8.ToArray();

    private int _content; //reasoning_content:已经在用的那一个
    private int _renamed; //reasoning / reasoning_text:被清洗层改名成 reasoning_content
    private int _details; //reasoning_details:结构化思考数组,目前完全丢弃
    private int _encrypted; //其中的加密条目

    /// <summary>
    /// 观察一行（SSE 的一行，或一段非流式 JSON），累加计数
    /// </summary>
    /// <param name="line">原始字节</param>
    public void Observe(ReadOnlySpan<byte> line)
    {
        bool hasDetails = false;
        ReadOnlySpan<byte> rest = line;
        while (true)
        {
            int at = rest.IndexOf(KeyPrefix);
            if (at < 0) break;
            rest = rest[(at + KeyPrefix.Length)..];
            if (rest.StartsWith(ContentSuffix)) _content++;
            else if (rest.StartsWith(DetailsSuffix))
            {
                _details++;
                hasDetails = true;
            }
            else if (rest.StartsWith(EncryptedDetailSuffix)) _encrypted++;
            else if (rest.StartsWith(TextSuffix) || rest.StartsWith(BareSuffix)) _renamed++;
        }

        // 加密条目只在 reasoning_details 里有意义,别在正文里误数用户说的话
        if (hasDetails) _encrypted += Count(line, EncryptedEntryKey);
    }

    /// <summary>
    /// 一行诊断文本。一个都没见到时也返回（全是 0 本身就是答案：这条流没有思考字段）
    /// </summary>
    /// <returns>形如 <c>reasoning fields: reasoning_content=12, reasoning/reasoning_text=0, reasoning_details=0, encrypted=0</c></returns>
    public string Describe() =>
        $"reasoning fields: reasoning_content={_content}, reasoning/reasoning_text={_renamed}, " +
        $"reasoning_details={_details}, encrypted={_encrypted}";

    private static int Count(ReadOnlySpan<byte> line, ReadOnlySpan<byte> token)
    {
        int count = 0;
        while (true)
        {
            int at = line.IndexOf(token);
            if (at < 0) return count;
            count++;
            line = line[(at + token.Length)..];
        }
    }
}
