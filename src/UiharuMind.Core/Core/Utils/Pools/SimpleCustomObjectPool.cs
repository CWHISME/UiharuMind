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

namespace UiharuMind.Core.Core.Utils
{
    public static class SimpleCustomObjectPool<T> where T : IPoolAble, new()
    {
        private static readonly Stack<T> Pool = new Stack<T>(10);

        public static T Get()
        {
            if (!Pool.TryPop(out var obj))
            {
                obj = new T();
            }

            return obj;
        }

        public static void Release(T obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Cannot release a null object.");
            }

            obj.Reset();
            Pool.Push(obj);
        }

        public static void ClearAll()
        {
            Pool.Clear();
        }
    }

    public interface IPoolAble
    {
        void Reset();
    }
}