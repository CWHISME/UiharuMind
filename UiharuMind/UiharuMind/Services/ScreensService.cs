using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using UiharuMind.Core.Input;

namespace UiharuMind.Services;

public class ScreensService
{
    private readonly Window _target;

    public PixelPoint MousePosition => new PixelPoint(InputManager.MouseData.X, InputManager.MouseData.Y);

    public ScreensService(Window target)
    {
        _target = target;
    }

    /// <summary>
    /// 获取当前鼠标所在的屏幕
    /// </summary>
    /// <returns></returns>
    public Screen? MouseScreen => _target.Screens.ScreenFromPoint(MousePosition);

    /// <summary>
    /// 当前鼠标所在屏幕
    /// </summary>
    public int MouseScreenId => IndexOfScreen(_target.Screens.All, MousePosition) + 1;

    public int IndexOfScreen(IReadOnlyList<Screen> list, PixelPoint point)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }
}