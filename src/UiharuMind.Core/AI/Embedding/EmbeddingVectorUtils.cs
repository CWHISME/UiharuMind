/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Text.Json;

namespace UiharuMind.Core.AI.Embedding;

internal static class EmbeddingVectorUtils
{
    /// <summary>
    /// 读响应里的一条向量（OpenAI 兼容的 embedding 数组）
    /// </summary>
    /// <param name="embedding">向量所在的 JSON 节点</param>
    /// <returns>向量；节点为 null 时返回 null</returns>
    public static float[]? Read(JsonElement embedding)
    {
        if (embedding.ValueKind == JsonValueKind.Null) return null;
        float[] vector = new float[embedding.GetArrayLength()];
        int i = 0;
        foreach (JsonElement value in embedding.EnumerateArray()) vector[i++] = value.GetSingle();
        return vector;
    }

    public static void NormalizeInPlace(float[] vector)
    {
        double sum = 0;
        foreach (float value in vector)
            sum += value * value;

        double magnitude = Math.Sqrt(sum);
        if (magnitude <= double.Epsilon) return;

        for (int i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] / magnitude);
    }
}
