namespace UiharuMind.Core.Core.Utils
{
    public class SimpleCustomObjectPool<T> where T : new()
    {
        private readonly Stack<T> Pool = new Stack<T>(10);
        private readonly Func<T> CreateFunc;

        public SimpleCustomObjectPool(Func<T> createFunc)
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