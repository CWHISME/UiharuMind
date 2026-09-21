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

namespace UiharuMind.Core.Core.Singletons
{
    /// <summary>
    /// 懒加载单例基类。
    ///
    /// <b>发布顺序的保证</b>：构造与 <see cref="IInitialize.OnInitialize"/> 都在锁内跑完之后
    /// 才写 <c>_instance</c>，而该字段是 <c>volatile</c>——写入带释放语义、无锁快速路径的读带获取语义，
    /// 因此读到非空引用时初始化期的全部写入必然也已可见。并发首次取用只有两种结果：
    /// 要么看不见（去锁上排队），要么看见的是成品。
    ///
    /// 旧实现是「先发布再初始化」且锁外读普通字段，于是并发首次取用会拿到半初始化实例
    /// （实测：并行测试里角色库刚 new 出来就被别的线程读走，字典还是空的，约 1/6 概率红）。
    ///
    /// <b>代价</b>：<c>OnInitialize</c>（含构造函数）不得直接或间接回读任何单例的 <c>Instance</c>
    /// 而形成环或自环——Monitor 对同线程可重入，重入时快速路径仍见 <c>null</c>，会二次构造出两个实例。
    /// 当前初始化期依赖图只有 <c>CharacterManager → DefaultCharacterManager</c> 一条边，无环。
    /// </summary>
    /// <typeparam name="T">单例类型，一般即派生类自身</typeparam>
    public class Singleton<T> where T : class, new()
    {
        private static volatile T? _instance;
        private static readonly object _locker = new object();

        public static T Instance
        {
            get
            {
                // 无锁快速路径:volatile 读,非空即代表初始化已完成且对本线程可见
                T? instance = _instance;
                if (instance != null) return instance;

                lock (_locker)
                {
                    if (_instance != null) return _instance;

                    T created = new T();
                    if (created is IInitialize initialize) initialize.OnInitialize();
                    // 全部初始化完成之后才发布,别的线程不可能观察到中间态
                    _instance = created;
                    return created;
                }
            }
        }
    }
}
