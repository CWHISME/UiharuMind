using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Core.Localization
{
    public class LanguageManager : Singleton<LanguageManager>
    {
        private Dictionary<string, string>? _languageDictionary;

        public void Load(string str)
        {
            string[] lines = str.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _languageDictionary?.Clear();
            _languageDictionary ??= new Dictionary<string, string>(lines.Length);
            foreach (string line in lines)
            {
                string[] keyValue = line.Split('=');
                if (keyValue.Length == 1) continue;
                _languageDictionary[keyValue[0]] = keyValue[1];
            }
        }

        public bool Has(string key)
        {
            return _languageDictionary?.ContainsKey(key) ?? false;
        }

        public string Get(string key)
        {
            if (_languageDictionary == null || _languageDictionary.Count == 0) return key;
            return _languageDictionary.GetValueOrDefault(key, key);
        }

        public string Get(string key, object param1)
        {
            return string.Format(Get(key), param1);
        }

        public string Get(string key, object param1, object param2)
        {
            return string.Format(Get(key), param1, param2);
        }

        public string Get(string key, object param1, object param2, object param3)
        {
            return string.Format(Get(key), param1, param2, param3);
        }

        public string Get(string key, params object[] param)
        {
            return string.Format(Get(key), param);
        }
    }
}