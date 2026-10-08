using System.Text;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 用户在设置里填的「额外启动参数」怎么变成逐项参数，各引擎共用
/// </summary>
public static class EngineArguments
{
    /// <summary>
    /// 按空白拆参数，双引号内的空白不拆
    /// </summary>
    /// <param name="text">参数文本</param>
    /// <returns>逐项参数</returns>
    public static IReadOnlyList<string> Split(string? text)
    {
        List<string> result = [];
        StringBuilder current = new();
        bool inQuotes = false;
        bool hasToken = false;
        foreach (char c in text ?? "")
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) result.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken) result.Add(current.ToString());
        return result;
    }
}
