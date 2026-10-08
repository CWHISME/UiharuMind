namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一段音频的起止（采样下标，左闭右开）
/// </summary>
/// <param name="Start">起</param>
/// <param name="End">止</param>
public readonly record struct AudioSpan(int Start, int End)
{
    public int Length => End - Start;
}

/// <summary>
/// 按停顿把长音频切成段：够了下限之后遇到第一个像样的停顿（0.3 秒）就在停顿中间下刀——一段大致是一句；
/// 到上限还没停顿就在最安静处下刀，免得把一个字切成两半。整段几乎无声的丢掉（不必拿去识别）
/// </summary>
public static class SpeechSegmenter
{
    private const float SilencePeak = 0.01f; //峰值低于它算整段无声
    private const int PauseFrames = 15; //连续这么多帧安静算一次停顿（0.3 秒）

    /// <summary>
    /// 切段
    /// </summary>
    /// <param name="samples">单声道采样</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="minLength">最短（剩下不足这么长的并进上一段）</param>
    /// <param name="maxLength">最长</param>
    /// <returns>有声的段，按时间顺序</returns>
    public static IReadOnlyList<AudioSpan> Split(ReadOnlySpan<float> samples, int sampleRate, TimeSpan minLength,
        TimeSpan maxLength)
    {
        int frame = Math.Max(1, sampleRate / 50); //20 毫秒一帧
        int min = Math.Max(frame, (int)(minLength.TotalSeconds * sampleRate));
        int max = Math.Max(min + frame, (int)(maxLength.TotalSeconds * sampleRate));
        List<AudioSpan> spans = [];
        int start = 0;
        while (start < samples.Length)
        {
            // 剩下的不到上限也照样在停顿处切：字幕的最后几句不该并成一条
            bool rest = samples.Length - start <= max;
            int end = FindCut(samples, start + min, rest ? samples.Length : start + max, frame, out bool paused);
            if (rest && !paused) end = samples.Length;
            if (samples.Length - end < min) end = samples.Length; //尾巴太短就不单成一段
            if (Peak(samples[start..end]) >= SilencePeak) spans.Add(new AudioSpan(start, end));
            start = end;
        }

        return spans;
    }

    // 窗口里第一个停顿的中间；没有停顿就取能量最低那一帧（同样安静取靠后的）
    private static int FindCut(ReadOnlySpan<float> samples, int from, int to, int frame, out bool paused)
    {
        paused = false;
        double quiet = SilencePeak * SilencePeak * frame; //整帧都在无声线以下
        int best = to;
        double bestEnergy = double.MaxValue;
        int pauseStart = -1;
        for (int at = from; at + frame <= to; at += frame)
        {
            double energy = 0;
            foreach (float sample in samples.Slice(at, frame)) energy += sample * sample;
            if (energy < quiet)
            {
                if (pauseStart < 0) pauseStart = at;
                if ((at - pauseStart) / frame + 1 >= PauseFrames)
                {
                    paused = true;
                    return pauseStart + PauseFrames * frame / 2;
                }
            }
            else
            {
                pauseStart = -1;
            }

            if (energy > bestEnergy) continue;
            bestEnergy = energy;
            best = at + frame / 2;
        }

        return best;
    }

    private static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0;
        foreach (float sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        return peak;
    }
}
