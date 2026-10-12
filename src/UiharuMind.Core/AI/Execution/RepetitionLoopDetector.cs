using System.Text;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 认出模型陷入复读：最近 <see cref="WindowChars"/> 字是同一小段（周期不超过 <see cref="MaxPeriod"/>）原样重复。
/// 实测有模型思考到一半开始无限输出「：**」，十几分钟攒出几十万字才停；正常行文不会出现这么长的严格周期。
/// 一路流一个实例（思考与正文各一个，不能混着喂）。
/// </summary>
internal sealed class RepetitionLoopDetector
{
    /// <summary>判定窗口：这么长的尾巴整段是严格周期才算复读</summary>
    public const int WindowChars = 4096;

    /// <summary>最长周期：周期再长就不像复读，而且窗口里至少要重复十几遍才够判</summary>
    public const int MaxPeriod = 256;

    private const int CheckIntervalChars = 256; //每攒这么多新字检查一次

    private readonly StringBuilder _tail = new();
    private int _sinceCheck;

    /// <summary>
    /// 喂一段流式增量
    /// </summary>
    /// <param name="delta">新增文本</param>
    /// <returns>认出复读时返回重复单元，否则为 null</returns>
    public string? Feed(string delta)
    {
        _tail.Append(delta);
        if (_tail.Length > WindowChars * 2) _tail.Remove(0, _tail.Length - WindowChars);

        _sinceCheck += delta.Length;
        if (_sinceCheck < CheckIntervalChars || _tail.Length < WindowChars) return null;
        _sinceCheck = 0;

        string window = _tail.ToString(_tail.Length - WindowChars, WindowChars);
        int period = FindPeriod(window);
        return period > 0 ? window[^period..] : null;
    }

    // 整个窗口满足 w[i] == w[i - p] 的最小 p；正常文本几乎每个 p 都在末尾一两个字就对不上，开销很小
    private static int FindPeriod(string window)
    {
        for (int period = 1; period <= MaxPeriod; period++)
        {
            int i = window.Length - 1;
            while (i >= period && window[i] == window[i - period]) i--;
            if (i < period) return period;
        }

        return 0;
    }
}
