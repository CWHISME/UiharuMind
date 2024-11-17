using System;
using System.Collections.ObjectModel;

namespace UiharuMind.Utils;

public static class ObservableCollectionExt
{
    public static void Remvoe<T>(this ObservableCollection<T> collection, Func<T, bool> predicate)
    {
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (predicate(collection[i]))
            {
                collection.RemoveAt(i);
                return;
            }
        }
    }

    public static void RemvoeAll(this ObservableCollection<object> collection, Func<object, bool> predicate)
    {
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (predicate(collection[i]))
            {
                collection.RemoveAt(i);
            }
        }
    }
}