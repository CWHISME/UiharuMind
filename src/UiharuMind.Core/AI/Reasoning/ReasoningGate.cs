namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 票据的模型归属门禁：<b>provider 与 model 都要对上</b>才发。
///
/// 为什么要有这道门禁：票据是「只有原厂认得的凭证」，换模型就没有意义。
/// 而<b>网关自己不校验</b>——实测把某模型的票发给另一个模型照样 200。
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
    /// 「说不出是哪家」——老版本落的票用的是这个值。
    ///
    /// 那时 <c>ProviderId</c> 取的是外层容器类名，所有远程供应商都叫同一个名字，
    /// 于是那张票<b>没有区分力</b>：它只记得模型，记不得是谁签的。
    /// 这类票不能按「双等」判（provider 恒不相等），只能退到只比 model——
    /// 那正是它当年实际能做到的全部。
    ///
    /// 不这么兼容的代价：所有老会话的加密票据一次性全部失效，推理链集体断掉，
    /// 而它们本来就没被门禁保护过（provider 那时恒等，门禁形同虚设）。
    /// </summary>
    internal const string UnknownProvider = nameof(Models.RemoteModelInfo);

    /// <summary>
    /// 本次请求的模型能不能用这张票。
    /// </summary>
    /// <param name="provider">本次请求的供应商</param>
    /// <param name="model">本次请求的模型</param>
    /// <returns>两者都与产出方一致时为 true</returns>
    public bool Allows(string provider, string model) =>
        string.Equals(Model, model, StringComparison.Ordinal)
        && (string.Equals(Provider, provider, StringComparison.Ordinal)
            || string.Equals(Provider, UnknownProvider, StringComparison.Ordinal));
}
