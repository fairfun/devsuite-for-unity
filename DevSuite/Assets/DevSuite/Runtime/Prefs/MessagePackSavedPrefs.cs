#if DEVSUITE_MESSAGEPACK
using MessagePack;
using System.IO;
using System.Threading.Tasks;

namespace Ff.Prefs
{
    internal class MessagePackSavedPrefs : SavedPrefs
    {
        private static readonly MessagePackSerializerOptions SerializationOptions =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        public MessagePackSavedPrefs(string name)
        {
            _serializer = (t, o) => MessagePackSerializer.Serialize(t, o, SerializationOptions);
            _deserializer = (t, b) => MessagePackSerializer.Deserialize(t, b, SerializationOptions);
            FilePath = Path.Combine(PersistentDataPath(), $"{name.TrimEnd('.')}.msgpack");
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