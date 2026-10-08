using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 配音工具的卡片上直接放产出的音频（与生图卡显示图同理）：收起时也在，放音与消息朗读同一路、互相打断
/// </summary>
public partial class ToolCallItem
{
    /// <summary>配好的音频文件（展开后的完整路径）；没有为 null</summary>
    public string? ResultAudioPath { get; private set; }

    /// <summary>有音频可放</summary>
    public bool HasResultAudio => ResultAudioPath != null;

    /// <summary>文件名</summary>
    public string ResultAudioName => Path.GetFileName(ResultAudioPath ?? "");

    /// <summary>
    /// 配音成功后按结果里的路径找文件；路径多是草稿目录简写，按会话的路径口径展开，文件已被删掉就不显示
    /// </summary>
    /// <param name="paths">会话的路径口径；拿不到时只认绝对路径</param>
    public void LoadResultAudio(AgentPathResolver? paths)
    {
        if (!IsSuccess || HasResultAudio || ToolName != SynthesizeSpeechTool.ToolName) return;
        string? saved = SynthesizeSpeechTool.ParseSavedPaths(ResultText).FirstOrDefault();
        if (saved == null) return;
        string full = paths != null && paths.TryResolve(saved, out string resolved) ? resolved : saved;
        if (!File.Exists(full)) return;
        ResultAudioPath = full;
        OnPropertyChanged(nameof(ResultAudioPath));
        OnPropertyChanged(nameof(HasResultAudio));
        OnPropertyChanged(nameof(ResultAudioName));
    }

    // 放与停走朗读那一路（会话接上朗读时才有回调）
    [RelayCommand]
    private void PlayResultAudio() => ReadAloudCallback?.Invoke(this);

    [RelayCommand]
    private void RevealResultAudio()
    {
        if (ResultAudioPath != null) FileOpener.RevealInFolder(ResultAudioPath);
    }
}
