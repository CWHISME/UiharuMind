using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.AutoClick;

/// <summary>
/// AutoClick editor preferences.
/// </summary>
public class AutoClickSettingConfig : TConfigBase<AutoClickSettingConfig>
{
    private bool _defaultRecordMouseMovement;
    private int _defaultMouseMovementFrameRate = 30;
    private bool _defaultRecordMouseMovementOnlyWhenPressed = true;

    /// <summary>
    /// Whether new AutoClick sessions should record mouse movement by default.
    /// </summary>
    public bool DefaultRecordMouseMovement
    {
        get => _defaultRecordMouseMovement;
        set
        {
            if (_defaultRecordMouseMovement == value) return;
            _defaultRecordMouseMovement = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>
    /// The default mouse movement record/playback frame rate for new AutoClick sessions.
    /// </summary>
    public int DefaultMouseMovementFrameRate
    {
        get => _defaultMouseMovementFrameRate;
        set
        {
            var frameRate = Math.Clamp(value, 1, 120);
            if (_defaultMouseMovementFrameRate == frameRate) return;
            _defaultMouseMovementFrameRate = frameRate;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>
    /// Whether new AutoClick sessions should record mouse movement only while a mouse button is pressed.
    /// </summary>
    public bool DefaultRecordMouseMovementOnlyWhenPressed
    {
        get => _defaultRecordMouseMovementOnlyWhenPressed;
        set
        {
            if (_defaultRecordMouseMovementOnlyWhenPressed == value) return;
            _defaultRecordMouseMovementOnlyWhenPressed = value;
            OnPropertyChanged();
            Save();
        }
    }
}
