using System.Collections.Generic;
using System.Linq;
using SharpHook.Data;
using UiharuMind.Core.AutoClick;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.ViewModels.Extensions;

/// <summary>
/// AutoClickAction 和 AutoClickActionData 的转换扩展方法
/// </summary>
public static class AutoClickActionExtensions
{
    /// <summary>
    /// 将 ViewModel 的 Action 转换为数据对象
    /// </summary>
    public static AutoClickActionData ToData(this AutoClickAction action)
    {
        return new AutoClickActionData
        {
            ActionType = action.ActionType,
            Description = action.Description,
            Delay = action.Delay,
            MouseButton = (int?)action.MouseButton,
            MouseX = action.MouseX,
            MouseY = action.MouseY,
            KeyCode = (int?)action.KeyCode,
            Text = action.Text,
            WheelDelta = action.WheelDelta,
            Duration = action.Duration
        };
    }

    /// <summary>
    /// 将数据对象转换为 ViewModel 的 Action
    /// </summary>
    public static AutoClickAction ToAction(this AutoClickActionData data)
    {
        var action = new AutoClickAction(data.ActionType, data.Description, data.Delay)
        {
            MouseButton = data.MouseButton.HasValue ? (MouseButton)data.MouseButton.Value : null,
            MouseX = data.MouseX,
            MouseY = data.MouseY,
            KeyCode = data.KeyCode.HasValue ? (KeyCode)data.KeyCode.Value : null,
            Text = data.Text,
            WheelDelta = data.WheelDelta,
            Duration = data.Duration
        };
        return action;
    }

    /// <summary>
    /// 批量转换：Action 列表 -> Data 列表
    /// </summary>
    public static List<AutoClickActionData> ToDataList(this IEnumerable<AutoClickAction> actions)
    {
        return actions.Select(a => a.ToData()).ToList();
    }

    /// <summary>
    /// 批量转换：Data 列表 -> Action 列表
    /// </summary>
    public static List<AutoClickAction> ToActionList(this IEnumerable<AutoClickActionData> dataList)
    {
        return dataList.Select(d => d.ToAction()).ToList();
    }
}
