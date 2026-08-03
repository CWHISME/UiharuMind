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
    /// <summary>
    /// 收藏的模型名列表,顺序即自动选择优先级
    /// </summary>
    public List<string> FavoriteModels { get; set; } = new();

    /// <summary>
    /// 是否已收藏指定模型
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <returns>已收藏返回True</returns>
    public bool IsFavorite(string modelName) => FavoriteModels.Contains(modelName);

    /// <summary>
    /// 切换指定模型的收藏状态
    /// </summary>
    /// <param name="modelName">模型名</param>
    public void ToggleFavorite(string modelName)
    {
        if (IsFavorite(modelName)) FavoriteModels.Remove(modelName);
        else FavoriteModels.Add(modelName);
        OnPropertyChanged(nameof(FavoriteModels));
    }

    //内置模型目录路径(不可修改)
    [JsonIgnore] public string DefaultLocalModelPath { get; set; } = "./InternalModels";

    //外部模型目录路径(可修改)
    public string LocalModelPath { get; set; } = Path.Combine(SettingConfig.RootDataPath, "Models");

    //之前扫描到的模型信息
    public Dictionary<string, GGufModelInfo> ModelInfos { get; set; } = new();
}