using System.Collections.Concurrent;
using System.Text;
using AngleSharp.Text;

namespace UiharuMind.Core.Core.Utils
{
    public static class StringBuilderPool
    {
        private static readonly ConcurrentStack<StringBuilder> Pool = new();

        public static StringBuilder Get()
        {
            if (!Pool.TryPop(out var obj))
            {
                obj = new StringBuilder();
            }

            return obj;
        }

        public static void Release(StringBuilder obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Cannot release a null object.");
            }

            obj.Clear();
            Pool.Push(obj);
        }
    }
}