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

namespace UiharuMind.Core.AI.Embedding;

internal static class EmbeddingVectorUtils
{
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
