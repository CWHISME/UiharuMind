/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 音频扩展：配音要知道「自己」是谁（不点名时用自己的声音）；没有角色时只给转写
/// </summary>
public sealed class AudioExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly AudioExtension Spec = new();

    public override string Name => BuiltinExtensions.Audio;

    public override string Guide =>
        "synthesize 用角色的声音把文字合成 WAV 文件（不出声播放）；" +
        "transcribe 把音频文件转成文字，可选带 SRT 字幕。结果保存在草稿目录下。";

    public override string Summary => "用角色的声音配音成音频文件；把音频转写成文字或字幕";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.AudioSynthesize, SynthesizeSpeechTool.ToolName),
        (BuiltinExtensions.AudioTranscribe, TranscribeAudioTool.ToolName),
    ];

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        List<AITool> tools = new();
        if (resources.Character != null) tools.Add(SynthesizeSpeechTool.Create(resources.Paths, resources.Character));
        tools.Add(TranscribeAudioTool.Create(resources.Paths));
        AudioExtension assembled = new();
        assembled.SetCommands(Bind(tools));
        return assembled;
    }
}
