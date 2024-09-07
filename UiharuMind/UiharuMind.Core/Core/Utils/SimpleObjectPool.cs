namespace UiharuMind.Core.Core.Utils
{
    public static class SimpleObjectPool<T> where T : IPoolAble, new()
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