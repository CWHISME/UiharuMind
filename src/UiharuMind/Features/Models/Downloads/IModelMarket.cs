using System.ComponentModel;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 「获取模型」里的一个分类（GGUF 仓库、语音模型……）：左栏列表与右栏详情都由它出数据，下载区各分类共用。
/// 视图按具体类型挑模板，新增一种本地模型只需加一个实现、它的列表与详情两块视图，再在组装处把它放进分类表
/// </summary>
public interface IModelMarket : INotifyPropertyChanged
{
    /// <summary>
    /// 分类名
    /// </summary>
    string Title { get; }

    /// <summary>
    /// 右栏有详情（窄窗时据此把详情盖在列表上）
    /// </summary>
    bool HasDetail { get; }

    /// <summary>
    /// 右栏空着时的图标（Assets/Icons 下的名字）
    /// </summary>
    string IconName { get; }

    /// <summary>
    /// 右栏空着时的提示
    /// </summary>
    string EmptyHint { get; }

    /// <summary>
    /// 右栏详情；没选为 null（视图按详情的类型挑模板）
    /// </summary>
    object? Detail { get; }

    /// <summary>
    /// 被选中时调：首次加载；下载源等设置变了则重来
    /// </summary>
    void Activate();

    /// <summary>
    /// 关掉右栏详情
    /// </summary>
    void CloseDetail();
}
