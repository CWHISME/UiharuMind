namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 票据的模型归属门禁：<b>provider 与 model 都要对上</b>才发。
///
/// 为什么要有这道门禁：票据是「只有原厂认得的凭证」，换模型就没有意义。
/// 而<b>网关自己不校验</b>——实测把 Meta 的票发给 deepseek 照样 200。
/// 也就是说发错了不会报错，只会安静地错：模型拿到一段读不懂的密文，
/// 轻则忽略（白花钱），重则误解上下文。所以门禁只能在这一侧。
///
/// 只比 model id 不够：同一个模型 id 在不同供应商下是不同的东西，
/// 票是原厂签的，跨供应商同样不认。
/// </summary>
/// <param name="Provider">票据的产出方（签票的那家）</param>
/// <param name="Model">票据的产出模型</param>
internal readonly record struct ReasoningGate(string Provider, string Model)
{
    /// <summary>
    /// 本次请求的模型能不能用这张票。
    /// </summary>
    /// <param name="provider">本次请求的供应商</param>
    /// <param name="model">本次请求的模型</param>
    /// <returns>两者都与产出方一致时为 true</returns>
    public bool Allows(string provider, string model) =>
        string.Equals(Provider, provider, StringComparison.Ordinal) &&
        string.Equals(Model, model, StringComparison.Ordinal);
}
