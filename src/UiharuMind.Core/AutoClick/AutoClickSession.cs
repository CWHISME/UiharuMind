using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SharpHook.Data;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AutoClick;

public enum AutoClickStepKind
{
    MouseClick,
    MouseDown,
    MouseUp,
    MouseMove,
    MouseWheel,
    KeyClick,
    KeyDown,
    KeyUp,
    Text,
    Delay,
    Loop
}

public class AutoClickSession : IUniquieContainerItem
{
    public const int CurrentVersion = 2;

    public int Version { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public int RepeatCount { get; set; } = 1;

    public double PlaybackSpeed { get; set; } = 1.0;

    public int DefaultDelay { get; set; } = 100;

    public bool RecordMouseMovement { get; set; }

    public int MouseMovementFrameRate { get; set; }

    public bool? RecordMouseMovementOnlyWhenPressed { get; set; }

    public List<AutoClickStepData> Steps { get; set; } = new();

    [JsonIgnore] public int StepCount => CountSteps(Steps);

    private static int CountSteps(IEnumerable<AutoClickStepData> steps)
    {
        var count = 0;
        foreach (var step in steps)
        {
            count++;
            count += CountSteps(step.Children);
        }

        return count;
    }
}

public class AutoClickStepData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public AutoClickStepKind Kind { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int Delay { get; set; }

    public MouseButton? MouseButton { get; set; }

    public short X { get; set; }

    public short Y { get; set; }

    public KeyCode? KeyCode { get; set; }

    public string? Text { get; set; }

    public int? WheelDelta { get; set; }

    public int? Duration { get; set; }

    public int LoopCount { get; set; } = 1;

    public List<AutoClickStepData> Children { get; set; } = new();
}
