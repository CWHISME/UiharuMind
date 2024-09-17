using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.ViewModels.ViewData;

public class LLamaCppSettingModel : ObservableObject
{
    private LLamaCppServerConfig _data;

    public LLamaCppServerConfig ServerSettingData
    {
        get => _data;
        set => _data = value;
    }

    //==================常规设置==================================
    public bool LogVerbose
    {
        get => _data.LogVerbose;
        set
        {   
            if (_data.LogVerbose != value)
            {
                _data.LogVerbose = value;
                OnPropertyChanged();
            }
        }
    }
    
    public bool LogPrefix
    {
        get => _data.LogPrefix;
        set
        {
            if (_data.LogPrefix != value)
            {
                _data.LogPrefix = value;
                OnPropertyChanged();
            }
        }
    }
    
    //=========================================================


    public int Threads
    {
        get => _data.Threads;
        set
        {
            if (_data.Threads != value)
            {
                _data.Threads = value;
                OnPropertyChanged();
            }
        }
    }

    public string CpuMask
    {
        get => _data.CpuMask;
        set
        {
            if (_data.CpuMask != value)
            {
                _data.CpuMask = value;
                OnPropertyChanged();
            }
        }
    }

    public string CpuRange
    {
        get => _data.CpuRange;
        set
        {
            if (_data.CpuRange != value)
            {
                _data.CpuRange = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseStrictCpuPlacement
    {
        get => _data.CpuStrict;
        set
        {
            if (_data.CpuStrict != value)
            {
                _data.CpuStrict = value;
                OnPropertyChanged();
            }
        }
    }

    public int Priority
    {
        get => _data.Priority;
        set
        {
            if (_data.Priority != value)
            {
                _data.Priority = value;
                OnPropertyChanged();
            }
        }
    }

    public int ContextSize
    {
        get => _data.ContextSize;
        set
        {
            if (_data.ContextSize != value)
            {
                _data.ContextSize = value;
                OnPropertyChanged();
            }
        }
    }

    public int TokensToPredict
    {
        get => _data.TokensToPredict;
        set
        {
            if (_data.TokensToPredict != value)
            {
                _data.TokensToPredict = value;
                OnPropertyChanged();
            }
        }
    }

    public int BatchSize
    {
        get => _data.BatchSize;
        set
        {
            if (_data.BatchSize != value)
            {
                _data.BatchSize = value;
                OnPropertyChanged();
            }
        }
    }

    public int PhysicalBatchSize
    {
        get => _data.PhysicalBatchSize;
        set
        {
            if (_data.PhysicalBatchSize != value)
            {
                _data.PhysicalBatchSize = value;
                OnPropertyChanged();
            }
        }
    }

    public bool EnableFlashAttention
    {
        get => _data.EnableFlashAttention;
        set
        {
            if (_data.EnableFlashAttention != value)
            {
                _data.EnableFlashAttention = value;
                OnPropertyChanged();
            }
        }
    }

    public int RoPEScalingMethod
    {
        get => _data.RoPEScalingMethod;
        set
        {
            if (_data.RoPEScalingMethod != value)
            {
                _data.RoPEScalingMethod = value;
                OnPropertyChanged();
            }
        }
    }

    public double RoPEContextScalingFactor
    {
        get => _data.RoPEContextScalingFactor;
        set
        {
            if (_data.RoPEContextScalingFactor != value)
            {
                _data.RoPEContextScalingFactor = value;
                OnPropertyChanged();
            }
        }
    }

    public double RoPEBaseFrequency
    {
        get => _data.RoPEBaseFrequency;
        set
        {
            if (_data.RoPEBaseFrequency != value)
            {
                _data.RoPEBaseFrequency = value;
                OnPropertyChanged();
            }
        }
    }

    public int YaRNOriginalContextSize
    {
        get => _data.YaRNOriginalContextSize;
        set
        {
            if (_data.YaRNOriginalContextSize != value)
            {
                _data.YaRNOriginalContextSize = value;
                OnPropertyChanged();
            }
        }
    }

    public double YaRNextrapolationMixFactor
    {
        get => _data.YaRNextrapolationMixFactor;
        set
        {
            if (_data.YaRNextrapolationMixFactor != value)
            {
                _data.YaRNextrapolationMixFactor = value;
                OnPropertyChanged();
            }
        }
    }

    public double YaRNAttentionFactor
    {
        get => _data.YaRNAttentionFactor;
        set
        {
            if (_data.YaRNAttentionFactor != value)
            {
                _data.YaRNAttentionFactor = value;
                OnPropertyChanged();
            }
        }
    }

    public double YaRNBetaSlow
    {
        get => _data.YaRNBetaSlow;
        set
        {
            if (_data.YaRNBetaSlow != value)
            {
                _data.YaRNBetaSlow = value;
                OnPropertyChanged();
            }
        }
    }

    public double YaRNBetaFast
    {
        get => _data.YaRNBetaFast;
        set
        {
            if (_data.YaRNBetaFast != value)
            {
                _data.YaRNBetaFast = value;
                OnPropertyChanged();
            }
        }
    }

    public int GpuLayers
    {
        get => _data.GpuLayers;
        set
        {
            if (_data.GpuLayers != value)
            {
                _data.GpuLayers = value;
                OnPropertyChanged();
            }
        }
    }

    public int SplitMode
    {
        get => _data.SplitMode;
        set
        {
            if (_data.SplitMode != value)
            {
                _data.SplitMode = value;
                OnPropertyChanged();
            }
        }
    }

    public string TensorSplit
    {
        get => _data.TensorSplit;
        set
        {
            if (_data.TensorSplit != value)
            {
                _data.TensorSplit = value;
                OnPropertyChanged();
            }
        }
    }

    public int MainGpuIndex
    {
        get => _data.MainGpuIndex;
        set
        {
            if (_data.MainGpuIndex != value)
            {
                _data.MainGpuIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CheckModelTensors
    {
        get => _data.CheckModelTensors;
        set
        {
            if (_data.CheckModelTensors != value)
            {
                _data.CheckModelTensors = value;
                OnPropertyChanged();
            }
        }
    }
}