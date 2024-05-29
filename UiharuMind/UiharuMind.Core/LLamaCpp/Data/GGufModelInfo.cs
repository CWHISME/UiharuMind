using System.Text.Json.Serialization;

namespace UiharuMind.Core.LLamaCpp.Data;

public class GGufModelInfo
{
    [JsonInclude] private Dictionary<string, string> Infos { get; set; } = new Dictionary<string, string>(10);

    public void UpdateValue(string lineInfo)
    {
        var input = lineInfo.AsSpan();

        int colonIndex = input.LastIndexOf(':');
        if (colonIndex >= 0)
        {
            colonIndex++;
            while (colonIndex < input.Length && char.IsWhiteSpace(input[colonIndex]))
            {
                colonIndex++;
            }

            // 查找等号的位置
            int eqIndex = input.Slice(colonIndex).IndexOf('=');
            if (eqIndex >= 0)
            {
                // 计算键和值的起始和结束位置
                int keyStartIndex = colonIndex;
                int keyEndIndex = colonIndex + eqIndex;
                int valueEndIndex = colonIndex + eqIndex + 1;

                // 跳过等号后面的空格
                while (valueEndIndex < input.Length && char.IsWhiteSpace(input[valueEndIndex]))
                {
                    valueEndIndex++;
                }

                // 提取键和值
                ReadOnlySpan<char> keySpan = input.Slice(keyStartIndex, keyEndIndex - keyStartIndex).Trim();
                ReadOnlySpan<char> valueSpan = input.Slice(valueEndIndex).Trim();

                Infos[keySpan.ToString()] = valueSpan.ToString();
            }
        }
    }
}