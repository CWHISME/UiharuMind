using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// llama.cpp 分片文件名：&lt;名字&gt;-00001-of-00003.gguf。llama-server 只要第一片，其余按名字自己找
/// </summary>
public static partial class GGufSplitName
{
    /// <summary>
    /// 解析分片文件名
    /// </summary>
    /// <param name="fileNameWithoutExtension">不带扩展名的文件名</param>
    /// <param name="baseName">去掉分片后缀的名字</param>
    /// <param name="index">片序，从 1 开始</param>
    /// <param name="count">总片数</param>
    /// <returns>是分片文件返回 true</returns>
    public static bool TryParse(string fileNameWithoutExtension, out string baseName, out int index, out int count)
    {
        baseName = fileNameWithoutExtension;
        index = 0;
        count = 0;
        Match match = SplitPattern().Match(fileNameWithoutExtension);
        if (!match.Success) return false;

        int parsedIndex = int.Parse(match.Groups[2].Value);
        int parsedCount = int.Parse(match.Groups[3].Value);
        if (parsedCount < 2 || parsedIndex < 1 || parsedIndex > parsedCount) return false;

        baseName = match.Groups[1].Value;
        index = parsedIndex;
        count = parsedCount;
        return true;
    }

    [GeneratedRegex(@"^(.+)-(\d{5})-of-(\d{5})$")]
    private static partial Regex SplitPattern();
}
