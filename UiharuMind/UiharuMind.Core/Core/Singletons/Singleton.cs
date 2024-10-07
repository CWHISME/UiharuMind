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
    public class Singleton<T> where T : new()
    {
        private static T _instance;
        private static object _locker = new object();

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_locker)
                    {
                        if (_instance == null)
                        {
                            _instance = new T();
                            if (_instance is IInitialize initialize) initialize.OnInitialize();
                        }

                        return _instance;
                    }
                }

                return _instance;
            }
        }
    }
}