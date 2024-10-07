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
    public class SimpleCustomFactObjectPool<T> where T : new()
    {
        private readonly Stack<T> Pool = new Stack<T>(10);
        private readonly Func<T> CreateFunc;

        public SimpleCustomFactObjectPool(Func<T> createFunc)
        {
            CreateFunc = createFunc;
        }

        public T Get()
        {
            if (!Pool.TryPop(out var obj))
            {
                obj = CreateFunc();
            }

            return obj;
        }

        public void Release(T obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Cannot release a null object.");
            }

            Pool.Push(obj);
        }

        public void ClearAll()
        {
            Pool.Clear();
        }
    }
}