using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 群头像：用前几位成员的头像拼一张。2 人左右各半，3 人左半 + 右侧上下两格，4 人及以上四宫格；
/// 画成方图，由圆形头像控件裁圆。进程级缓存（按成员与各自头像来源），不得释放
/// </summary>
public static class GroupAvatarComposer
{
    private const int Size = 96; //像素边长：列表里最大 38 逻辑像素，2x 屏也够
    private const double Gap = 1.5; //格与格之间留的缝

    private static readonly Dictionary<string, Bitmap> Cache = new();

    /// <summary>
    /// 拼群头像
    /// </summary>
    /// <param name="members">成员角色，按发言顺序；只用前四位</param>
    /// <returns>拼好的头像；没有成员时退默认头像</returns>
    public static Bitmap? Compose(IReadOnlyList<CharacterData> members)
    {
        List<(CharacterData Character, Bitmap Icon)> faces = members.Take(4)
            .Select(x => (x, IconUtils.GetCharacterBitmapOrDefault(x)))
            .Where(x => x.Item2 != null)
            .Select(x => (x.Item1, x.Item2!))
            .ToList();
        if (faces.Count == 0) return IconUtils.DefaultCharIcon;
        if (faces.Count == 1) return faces[0].Icon;

        // 头像换了图，来源串跟着变，键随之失效
        string key = string.Join("|", faces.Select(x => $"{x.Character.CharacterId}:{x.Character.CharacterIcon?.GetHashCode()}"));
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out Bitmap? cached)) return cached;

            RenderTargetBitmap bitmap = new(new PixelSize(Size, Size));
            using (var context = bitmap.CreateDrawingContext())
            {
                IReadOnlyList<Rect> cells = CellsFor(faces.Count);
                for (int i = 0; i < faces.Count; i++)
                {
                    Rect cell = cells[i];
                    context.DrawImage(faces[i].Icon, FillSource(faces[i].Icon.Size, cell.Size), cell);
                }
            }

            Cache[key] = bitmap;
            return bitmap;
        }
    }

    private static IReadOnlyList<Rect> CellsFor(int count)
    {
        const double half = Size / 2.0;
        const double gap = Gap / 2;
        Rect left = new(0, 0, half - gap, Size);
        Rect right = new(half + gap, 0, half - gap, Size);
        Rect topRight = new(half + gap, 0, half - gap, half - gap);
        Rect bottomRight = new(half + gap, half + gap, half - gap, half - gap);
        return count switch
        {
            2 => [left, right],
            3 => [left, topRight, bottomRight],
            _ => [new Rect(0, 0, half - gap, half - gap), topRight, new Rect(0, half + gap, half - gap, half - gap), bottomRight],
        };
    }

    // UniformToFill：从源图中间裁出与格子同比例的一块
    private static Rect FillSource(Size source, Size cell)
    {
        double scale = Math.Max(cell.Width / source.Width, cell.Height / source.Height);
        double width = cell.Width / scale;
        double height = cell.Height / scale;
        return new Rect((source.Width - width) / 2, (source.Height - height) / 2, width, height);
    }
}
