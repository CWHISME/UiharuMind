using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using UiharuMind.Core.Configs;

#pragma warning disable SKEXP0110

namespace UiharuMind.Core.AI.Character;

public class CharacterConfig
{
    public PromptTemplateConfig PromptConfig { get; set; } = new PromptTemplateConfig();
    // {
    //     InputVariables = new List<InputVariable> { "user", "char" },
    // };

    public ChatPromptExecutionSettings ExecutionSettings { get; set; } = new ChatPromptExecutionSettings();
    // public OpenAIPromptExecutionSettings OpenAiSettings { get; set; } = new OpenAIPromptExecutionSettings()
    // {
    //     Temperature = 1,
    //     TopP = 1,
    //     FrequencyPenalty = 0,
    //     PresencePenalty = 0,
    // };

    private ChatCompletionAgent? _cachedAgent;
    private OpenAIPromptExecutionSettings? _openAiSettings;

    /// <summary>
    /// 转为 ChatCompletionAgent
    /// </summary>
    /// <returns></returns>
    public ChatCompletionAgent ToAgent(Kernel kernel, Dictionary<string, object?>? kernelArguments = null)
    {
        if (_cachedAgent != null) return _cachedAgent;
        // if (kernelArguments != null)
        // {
        //     kernelArguments.Add("char", PromptConfig.Name);
        // }
        _openAiSettings ??= new OpenAIPromptExecutionSettings();
        _openAiSettings.Temperature = ExecutionSettings.Temperature;
        _openAiSettings.TopP = ExecutionSettings.TopP;
        _openAiSettings.FrequencyPenalty = ExecutionSettings.FrequencyPenalty;
        _openAiSettings.PresencePenalty = ExecutionSettings.PresencePenalty;

        _cachedAgent = new ChatCompletionAgent(PromptConfig)
        {
            Kernel = kernel,
            Arguments = new KernelArguments(_openAiSettings)
            // {
            //     { "user", "Cat" },
            //     { "char", PromptConfig.Name },
            // }
        };

        if (kernelArguments != null)
        {
            foreach (var args in kernelArguments)
            {
                _cachedAgent.Arguments.Add(args.Key, args.Value);
            }
        }

        return _cachedAgent;
    }
}