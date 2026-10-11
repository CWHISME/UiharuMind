using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 朗读工具：把话与语气交给会话、如实回报出没出声；会话那头第一个接手的说了算，没人接就是没出声
/// </summary>
public class SpeakToolTests
{
    [Fact]
    public async Task Tool_PassesTextAndTone_AndReportsWhetherItWasHeard()
    {
        List<SpeechRequest> requests = [];
        bool heard = true;
        AIFunction tool = (AIFunction)SpeakTool.Create(request =>
        {
            requests.Add(request);
            return Task.FromResult(heard);
        });

        string? spoken = (await tool.InvokeAsync(new AIFunctionArguments { ["text"] = " 你好呀 ", ["tone"] = "开心" },
            TestContext.Current.CancellationToken))?.ToString();
        heard = false;
        string? missed = (await tool.InvokeAsync(new AIFunctionArguments { ["text"] = "再见" },
            TestContext.Current.CancellationToken))?.ToString();

        Assert.Equal([new SpeechRequest("你好呀", "开心"), new SpeechRequest("再见")], requests);
        Assert.Contains("已朗读", spoken);
        Assert.Contains("未朗读", missed);
    }

    [Fact]
    public async Task Session_FirstHandlerThatSpeaksWins_NobodyMeansSilence()
    {
        ChatSession session = new("t", new CharacterData { CharacterId = "c", IsAgent = true }) { IsTransient = true };
        SpeechRequest request = new("hi");
        Assert.False(await session.RequestSpeechAsync(request));

        List<string> asked = [];
        session.SpeechRequested += _ =>
        {
            asked.Add("hidden");
            return Task.FromResult(false); //缓存着但没在显示的那份
        };
        session.SpeechRequested += _ =>
        {
            asked.Add("shown");
            return Task.FromResult(true);
        };

        Assert.True(await session.RequestSpeechAsync(request));
        Assert.Equal(["hidden", "shown"], asked);
    }
}
