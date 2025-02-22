/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.RemoteOpenAI;

public class RemoteModelInfo : ILlmModel
{
    public BaseRemoteModelConfig Config { get; set; } = new RemoteModelConfig();

    [JsonIgnore] public string ModelName => Config.ModelName;
    [JsonIgnore] public string ModelPath => Config.ModelPath;
    [JsonIgnore] public bool IsVision => Config.IsVision;
    [JsonIgnore] public string ModelDescription => Config.ModelDescription;
    [JsonIgnore] public string ModelId => Config.ModelId;
    [JsonIgnore] public int Port => Config.Port;
    
    private string _apiKey = "";

    public string ApiKey
    {
        get => AesEncryptionUtils.DecryptString(_apiKey);
        set => _apiKey = AesEncryptionUtils.EncryptString(value);
    }
}