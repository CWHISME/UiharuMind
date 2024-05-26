namespace UiharuMind.Core.Core.Singleton
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