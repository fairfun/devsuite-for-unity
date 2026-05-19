#if DEVSUITE_MEMORYPACK
using MemoryPack;
using System.IO;
using System.Threading.Tasks;

namespace Ff.Prefs
{
    internal class MemoryPackSavedPrefs : SavedPrefs
    {
        private static readonly MemoryPackSerializerOptions SerializationOptions = MemoryPackSerializerOptions.Utf8;

        public MemoryPackSavedPrefs(string name)
        {
            _serializer = (t, o) => MemoryPackSerializer.Serialize(t, o, SerializationOptions);
            _deserializer = (t, b) => MemoryPackSerializer.Deserialize(t, b, SerializationOptions);
            FilePath = Path.Combine(PersistentDataPath(), $"{name.TrimEnd('.')}.bin");
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