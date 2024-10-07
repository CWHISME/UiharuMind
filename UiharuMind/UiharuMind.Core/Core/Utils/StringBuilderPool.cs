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
                obj = new StringBuilder(8192);
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