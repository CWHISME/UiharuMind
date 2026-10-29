namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 思考参数在请求体里的写法。字段名没有任何统一来源——models.dev 只给「能选哪些值」，
/// 不给「字段叫什么」，所以只能按供应商族分派。
///
/// 预设供应商显式声明自己的格式；自定义配置按端点地址推断（见 <see cref="ThinkingFormats.Detect"/>）。
/// </summary>
public enum EThinkingFormat
{
    /// <summary>只发 <c>reasoning_effort</c>，关闭用 <c>reasoning_effort: none</c>。OpenAI 兼容端点的通用写法</summary>
    Effort,

    /// <summary>发 <c>thinking: {type: enabled|disabled}</c> 对象，有档位时再附 <c>reasoning_effort</c>。
    /// 智谱与 DeepSeek 同形（实测两家都认这两个字段）</summary>
    ThinkingType,

    /// <summary>发 <c>chat_template_kwargs.enable_thinking</c> 布尔。Agnes 走这条：
    /// 顶层 <c>thinking</c>/<c>reasoning_effort</c> 经它的 litellm 网关直接 400</summary>
    ChatTemplate,
}

/// <summary>思考格式的推断</summary>
public static class ThinkingFormats
{
    /// <summary>
    /// 按端点地址推断格式。只用于没显式声明格式的配置（自定义服务商）；
    /// 预设供应商各自覆写 <see cref="Configs.RemoteAI.BaseRemoteModelConfig.ThinkingFormat"/>，不走这里。
    /// </summary>
    /// <param name="modelPath">模型端点地址（完整 URL）</param>
    /// <returns>推断出的格式；认不出时给 OpenAI 兼容的通用写法</returns>
    public static EThinkingFormat Detect(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath)) return EThinkingFormat.Effort;

        string url = modelPath.ToLowerInvariant();
        if (url.Contains("bigmodel.cn") || url.Contains("api.z.ai")) return EThinkingFormat.ThinkingType;
        if (url.Contains("deepseek.com")) return EThinkingFormat.ThinkingType;
        if (url.Contains("agnes-ai")) return EThinkingFormat.ChatTemplate;
        return EThinkingFormat.Effort;
    }
}
