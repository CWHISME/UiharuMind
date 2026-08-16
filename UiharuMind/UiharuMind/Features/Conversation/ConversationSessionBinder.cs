/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Threading.Tasks;
using System.Threading;
using System;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话本体与执行者的挂接：建会话、装载会话、把界面当前的工作目录与权限档送进去。
///
/// 刻意<b>只管会话与执行者，不碰任何界面状态</b>——标题、当前角色、记忆库面板、
/// 属性变更通知全都留在视图模型里。所以这里的方法一律「参数进、会话出」，
/// 唯一的对外回调是执行者的忙碌态上报。
/// </summary>
public sealed class ConversationSessionBinder
{
    private const int TitleMaxLength = 30; //取自首句的标题长度上限

    private readonly Action _onBusyChanged;

    /// <param name="onBusyChanged">执行者忙碌态变化(预连、整理交接文档等具名等待)</param>
    public ConversationSessionBinder(Action onBusyChanged)
    {
        _onBusyChanged = onBusyChanged;
    }

    /// <summary>
    /// 新建会话并挂接其执行者。标题取自首句，<b>描述显式清空</b>——
    /// 标题已经取自这句话了，再存一份只会让列表副行重复显示同一句；
    /// 而且必须显式写，<see cref="ChatSession.Description"/> 的默认值是字面量 "Empty"，
    /// 省着不写会让副行显示这四个字母。
    /// </summary>
    /// <param name="character">会话角色</param>
    /// <param name="titleSeed">用来取标题的原文</param>
    /// <param name="workspacePath">界面当前的工作目录</param>
    /// <param name="permissionModeIndex">界面当前的权限档</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>已挂接的会话本体</returns>
    public async Task<ChatSession> CreateAsync(CharacterData character, string titleSeed,
        string? workspacePath, int permissionModeIndex, CancellationToken cancellationToken)
    {
        ChatSession created = new()
        {
            CharacterId = character.CharacterId,
            Title = TitleFrom(titleSeed),
            Description = string.Empty,
            WorkspacePath = workspacePath,
            PermissionModeIndex = permissionModeIndex,
        };
        SessionManager.Instance.Add(created);
        WatchBusy(created.Runner); //必须在 AttachAsync 之前,预连就在它里面
        await created.Runner.AttachAsync(created, cancellationToken);
        return created;
    }

    /// <summary>
    /// 加载会话本体并挂接其执行者。工作目录与权限档取自界面当前值，
    /// 变化时由执行者内部按装配指纹重建并迁移框架附加状态。
    /// </summary>
    /// <param name="meta">会话元数据</param>
    /// <param name="workspacePath">界面当前的工作目录</param>
    /// <param name="permissionModeIndex">界面当前的权限档</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>会话本体；文件缺失或损坏为 null</returns>
    public async Task<ChatSession?> AttachAsync(ChatSessionMeta meta, string? workspacePath,
        int permissionModeIndex, CancellationToken cancellationToken)
    {
        ChatSession? session = SessionManager.Instance.Load(meta.SessionId);
        if (session == null) return null;

        session.WorkspacePath = workspacePath;
        session.PermissionModeIndex = permissionModeIndex;
        // 回调要在 AttachAsync **之前**挂上:装配就发生在它里面,预连也在那儿,
        // 挂在后面的话第一次等待(恰好是唯一会等满十秒的那次)一声不响
        WatchBusy(session.Runner);
        await session.Runner.AttachAsync(session, cancellationToken);
        return session;
    }

    /// <summary>
    /// 把界面上改动的工作目录与权限档写回会话本体
    /// </summary>
    /// <param name="meta">会话元数据(已带上界面的新值)</param>
    public static void PersistSettings(ChatSessionMeta meta)
    {
        ChatSession? session = SessionManager.Instance.Load(meta.SessionId);
        if (session == null) return;
        session.WorkspacePath = meta.WorkspacePath;
        session.PermissionModeIndex = meta.PermissionModeIndex;
        session.SaveMeta(); //只动头字段,不必重写整份历史
    }

    /// <summary>
    /// 接上执行者的忙碌态上报。赋值而非累加订阅：一个会话一个执行者、一个视图，
    /// 重复挂接只会覆盖成同一个回调，不会攒出多份。
    /// </summary>
    /// <param name="runner">本会话的执行者</param>
    private void WatchBusy(ICharacterRunner runner)
    {
        runner.BusyChanged = _onBusyChanged;
    }

    private static string TitleFrom(string seed) =>
        seed.Length > TitleMaxLength ? seed[..TitleMaxLength] + "…" : seed;
}
