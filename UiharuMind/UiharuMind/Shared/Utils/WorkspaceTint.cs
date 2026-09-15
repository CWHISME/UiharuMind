/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Avalonia.Media;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 工作区 → 项目色的映射（类似 Rider 的项目名配色）：色相与饱和由工作区<b>完整路径</b>稳定决定
/// （与 <see cref="WorkspaceSegment"/> 同一哈希源），亮度按深浅主题取两套。
/// 同路径恒同色、换路径换色、跨进程不变。无工作区返回统一的中性灰。
///
/// <b>色相不做连续映射</b>——哈希直接 %360 是均匀随机分布，总会有些项目对子落在相邻色相上，
/// 视觉上「差异不大」；输入是完整路径还是目录名不影响这一点，SHA256 是雪崩式的，
/// 公共路径前缀不会稀释差异。这里把色相离散成 12 档、每档 30°，再按二级哈希档内微调 ±9°，
/// 尽力把项目间的色相拉开（相邻档最小差 12°）——不是保证性隔离：落进同一<b>档位且</b>同一
/// 饱和档的两个项目颜色完全相同，饱和度三档只是兜底维度，不是防撞承诺。
/// </summary>
public static class WorkspaceTint
{
    private const int HueBands = 12; //离散色相档数:30°一档
    private const double SaturationBase = 0.62;
    private const double SaturationStep = 0.05; //同档撞色时的第三维:饱和度三档
    private const double JitterSpan = 9; //档内微调幅度(±9°),小于半档 15°,不会串到隔壁档

    private static readonly object Gate = new();
    private static readonly Dictionary<int, (SolidColorBrush Light, SolidColorBrush Dark)> Cache = new();

    // 未绑工作区:两主题同灰。刻意不参与彩色——它不是项目,不该假装有归属
    private static readonly SolidColorBrush UnspecifiedLight = new(Color.FromRgb(0x8C, 0x8C, 0x8C));
    private static readonly SolidColorBrush UnspecifiedDark = new(Color.FromRgb(0x6E, 0x6E, 0x6E));

    /// <summary>
    /// 取项目色画刷。
    /// </summary>
    /// <param name="workspacePath">工作区完整路径；未绑定为 null 或空串 → 中性灰</param>
    /// <param name="dark">深/浅主题；缺省按当前实际主题</param>
    /// <returns>画刷（缓存实例，可安全复用）</returns>
    public static SolidColorBrush For(string? workspacePath, bool? dark = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            return (dark ?? ApplicationThemeManager.IsDarkTheme()) ? UnspecifiedDark : UnspecifiedLight;

        (SolidColorBrush light, SolidColorBrush darkBrush) = Get(workspacePath);
        return (dark ?? ApplicationThemeManager.IsDarkTheme()) ? darkBrush : light;
    }

    private static (SolidColorBrush Light, SolidColorBrush Dark) Get(string workspacePath)
    {
        (int hue, int satIndex) = PaletteOf(workspacePath);
        // 色相档内微调最多 ±9°、饱和只有三档:两者拼一个键不会互相覆盖
        int key = hue * 3 + satIndex;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out (SolidColorBrush Light, SolidColorBrush Dark) pair)) return pair;

            double sat = SaturationBase + satIndex * SaturationStep;
            pair = (ToBrush(hue, sat, lightness: 0.32), ToBrush(hue, sat, lightness: 0.72));
            Cache[key] = pair;
            return pair;
        }
    }

    /// <summary>
    /// 色相与饱和度都派生自 <see cref="WorkspaceSegment"/> 的哈希段（该段哈希的是<b>完整路径</b>）：
    /// 哈希串每 4 位 hex 取一个变量——色相档、档内微调、饱和档互不重叠。
    /// </summary>
    private static (int Hue, int SatIndex) PaletteOf(string workspacePath)
    {
        string segment = WorkspaceSegment.From(workspacePath);
        // 段名形如 client_0c65a812；纯哈希(目录名不可用)时 LastIndexOf 为 -1,+1 恰好从头取
        string hashPart = segment[(segment.LastIndexOf('_') + 1)..];
        uint h = Convert.ToUInt32(hashPart, 16);

        int band = (int)(h % HueBands);
        int hue = band * (360 / HueBands)
                  + (int)((h >> 4) % (uint)(JitterSpan * 2 + 1)) - (int)JitterSpan;
        int satIndex = (int)((h >> 12) % 3);
        return (hue, satIndex);
    }

    private static SolidColorBrush ToBrush(int hue, double saturation, double lightness)
    {
        (double r, double g, double b) = HslToRgb(hue / 360.0, saturation, lightness);
        return new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)));
    }

    private static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s == 0) return (l, l, l);

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return (HueToRgb(p, q, h + 1.0 / 3.0), HueToRgb(p, q, h), HueToRgb(p, q, h - 1.0 / 3.0));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}