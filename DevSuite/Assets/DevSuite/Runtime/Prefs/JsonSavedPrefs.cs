#if DEVSUITE_NEWTONSOFT
using Newtonsoft.Json;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Ff.Prefs
{
    internal class JsonSavedPrefs : SavedPrefs
    {
        public JsonSavedPrefs(string name)
        {
            _serializer = (t, o) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o, Formatting.Indented));
            _deserializer = (t, b) => JsonConvert.DeserializeObject(Encoding.UTF8.GetString(b), t);
            FilePath = Path.Combine(PersistentDataPath(), $"{name.TrimEnd('.')}.json");
            _ = Initialize();
        }

        protected override Task DoInitialize()
        {
            if (!Exists(FilePath))
            {
                _data = new DefaultSavedPrefsData();
                return Task.CompletedTask;
            }

            var bytes = File.ReadAllBytes(FilePath);
            _data = _deserializer(typeof(DefaultSavedPrefsData), bytes) as DefaultSavedPrefsData;
            return Task.CompletedTask;
        }

        protected override Task DoFlush()
        {
            EnsurePath(FilePath);

            var bytes = _serializer(typeof(DefaultSavedPrefsData), _data);
            File.WriteAllBytes(FilePath, bytes);

            return Task.CompletedTask;
        }

        private static bool Exists(string path)
        {
            return File.Exists(path);
        }

        private static void EnsurePath(string path)
        {
            var folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }
    }
}
#endif