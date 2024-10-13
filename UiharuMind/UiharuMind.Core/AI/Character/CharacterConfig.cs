using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace UiharuMind.Core.AI.Character;

public class CharacterConfig
{
    public PromptTemplateConfig PromptConfig { get; set; } = new PromptTemplateConfig();

    public OpenAIPromptExecutionSettings OpenAiSettings { get; set; } = new OpenAIPromptExecutionSettings()
    {
        Temperature = 0.5,
        MaxTokens = 2048,
        TopP = 0.6,
        FrequencyPenalty = 0,
        PresencePenalty = 0,
    };

    [Experimental("SKEXP0110")] private ChatCompletionAgent? _cachedAgent;

    /// <summary>
    /// 转为 ChatCompletionAgent
    /// </summary>
    /// <returns></returns>
    [Experimental("SKEXP0110")]
    public ChatCompletionAgent ToAgent()
    {
        if (_cachedAgent != null) return _cachedAgent;
        _cachedAgent = new ChatCompletionAgent(PromptConfig)
        {
            Arguments = new KernelArguments(OpenAiSettings)
            {
                { "user", "Cat" },
                { "char", PromptConfig.Name },
            }
        };
        return _cachedAgent;
    }
}