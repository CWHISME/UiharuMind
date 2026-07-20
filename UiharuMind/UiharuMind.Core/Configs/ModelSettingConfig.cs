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
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class ModelSettingConfig : TConfigBase<ModelSettingConfig>
{
    private string _favoriteModel = "";

    public string FavoriteModel
    {
        get => _favoriteModel;
        set
        {
            if (_favoriteModel == value) return;
            _favoriteModel = value;
            OnPropertyChanged();
        }
    }

    //内置模型目录路径(不可修改)
    [JsonIgnore] public string DefaultLocalModelPath { get; set; } = "./InternalModels";

    //外部模型目录路径(可修改)
    public string LocalModelPath { get; set; } = Path.Combine(SettingConfig.RootDataPath, "Models");

    //之前扫描到的模型信息
    public Dictionary<string, GGufModelInfo> ModelInfos { get; set; } = new();
}