using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ff.Prefs
{
    public interface ISavedPrefs
    {
        void SetBool(string key, bool? value);
        void SetInt(string key, int? value);
        void SetFloat(string key, float? value);
        void SetString(string key, string value);
        void SetObject<T>(string key, T value);
        bool? GetBool(string key, bool? defaultValue = default);
        int? GetInt(string key, int? defaultValue = default);
        float? GetFloat(string key, float? defaultValue = default);
        string GetString(string key, string defaultValue = default);
        T GetObject<T>(string key, T defaultValue = default);
        void DeleteKey(string key);
        void Flush();
        void Clear();
        Task EnsureReady();
        bool Ready { get; }
        void SetSerializer(SerializeFunction serialize, DeserializeFunction deserialize);
        public string SessionId { get; }
        public bool Disposed { get; }
    }

    public delegate byte[] SerializeFunction(Type type, object obj);
    public delegate object DeserializeFunction(Type type, byte[] data);

    public abstract class SavedPrefs : ISavedPrefs, IDisposable
    {
        private static string _persistentDataPath;
        protected static string PersistentDataPath()
        {
            _persistentDataPath ??=
#pragma warning disable CS0162
#if UNITY_WEBGL && !UNITY_EDITOR
                $"idbfs/{System.Text.RegularExpressions.Regex.Replace(Application.productName, @"[^a-zA-Z0-9\-_]+", "")}";
#else
                Application.persistentDataPath;
#endif
#pragma warning restore CS0162
            return _persistentDataPath;
        }

        private static Func<string, SavedPrefs> _factory;
        public static Func<string, SavedPrefs> Factory
        {
            get
            {
                return _factory ?? (name =>
                {
#pragma warning disable CS0162
#if DEVSUITE_MEMORYPACK
                    return new MemoryPackSavedPrefs(name);
#endif
#if DEVSUITE_MESSAGEPACK
                    return new MessagePackSavedPrefs(name);
#endif
#if DEVSUITE_NEWTONSOFT
                    return new JsonSavedPrefs(name);
#endif
                    Debug.LogWarning("It seems that neither com.cysharp.memorypack nor com.unity.nuget.newtonsoft-json is being used in the project. For SavedPrefs to be able to persistently save data you need to install one of them.");
                    return new FallbackSavedPrefs(name);
#pragma warning restore CS0162
                });
            }
            set
            {
                _factory = value;
            }
        }

        private string _sessionId;
        public string SessionId => _sessionId ??= Guid.NewGuid().ToString();

        public bool Disposed { get; private set; }

#if UNITY_EDITOR
        static SavedPrefs()
        {
            UnityEditor.EditorApplication.playModeStateChanged += m =>
            {
                if (m is UnityEditor.PlayModeStateChange.ExitingEditMode or UnityEditor.PlayModeStateChange.ExitingPlayMode)
                    ResetStatic();
            };
        }
#endif

        private static void ResetStatic()
        {
            _default?._data?.Clear();
            _default = null;
            _factory = null;
        }

        private static SavedPrefs _default;
        public static SavedPrefs Default
        {
            get
            {
                return _default ??= Factory.Invoke($"{nameof(SavedPrefs)}.Default");
            }
            set
            {
                _default = value;
            }
        }

        internal ISavedPrefsData _data;

        private Task _initializationTask;
        private bool _hasChanges;
        public bool Ready { get; private set; }

        protected DeserializeFunction _deserializer;
        protected SerializeFunction _serializer;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public async Task EnsureReady()
        {
            if (_initializationTask != null)
            {
                await _initializationTask;
                return;
            }

            _ = Initialize();
            await _initializationTask;
        }

        public void SetSerializer(SerializeFunction serialize, DeserializeFunction deserialize)
        {
            _serializer = serialize;
            _deserializer = deserialize;
        }

        protected async Task Initialize()
        {
            _initializationTask = DoInitialize();
            await _initializationTask;
            Ready = true;
        }

        protected abstract Task DoInitialize();

        public async void SetBool(string key, bool? value)
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;
            _data.Booleans[key] = value;
            ScheduleFlush();
        }

        public async void SetInt(string key, int? value)
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;
            _data.Integers[key] = value;
            ScheduleFlush();
        }

        public async void SetFloat(string key, float? value)
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;
            _data.Floats[key] = value;
            ScheduleFlush();
        }

        public async void SetString(string key, string value)
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;

            _data.Strings[key] = value;
            ScheduleFlush();
        }

        public async void SetObject<T>(string key, T value)
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;

            _data.Objects[key] = _serializer?.Invoke(typeof(T), value);
            ScheduleFlush();
        }

        public async void Clear()
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;
            _data.Clear();
            Flush();
        }

        public bool? GetBool(string key, bool? defaultValue = null)
        {
            if (!Ready)
                throw new Exception("Data is not initialized");

            if (_data.Booleans.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public int? GetInt(string key, int? defaultValue = null)
        {
            if (!Ready)
                throw new Exception("Data is not initialized");

            if (_data.Integers.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public float? GetFloat(string key, float? defaultValue = null)
        {
            if (!Ready)
                throw new Exception("Data is not initialized");

            if (_data.Floats.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public string GetString(string key, string defaultValue = null)
        {
            if (!Ready)
                throw new Exception("Data is not initialized");

            if (_data.Strings.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public T GetObject<T>(string key, T defaultValue = default)
        {
            if (!Ready)
                throw new Exception("Data is not initialized");

            if (_data.Objects.TryGetValue(key, out var value))
            {
                return (T)_deserializer.Invoke(typeof(T), value);
            }
            return defaultValue;
        }

        public async void DeleteKey(string key)
        {
            _hasChanges = true;
            if (!Ready)
                await _initializationTask;

            _data.Booleans.Remove(key);
            _data.Integers.Remove(key);
            _data.Floats.Remove(key);
            _data.Strings.Remove(key);
        }

        private bool _waitingFlush;

        private async void ScheduleFlush()
        {
            if (_waitingFlush)
                return;

            try
            {
                _waitingFlush = true;
                const int framesToSkip = 1;
                for (var i = 0; i < framesToSkip; i++)
                    await Task.Yield();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_waitingFlush) //can be set from Flush
                return;

            _waitingFlush = false;
            Flush();
        }

        private bool _flushing;

        public async void Flush()
        {
            _waitingFlush = false;
            if (_flushing)
                return;
            if (!Ready)
                await _initializationTask;
            if (!_hasChanges)
                return;

            _waitingFlush = false;
            _hasChanges = false;
            _flushing = true;
            await DoFlush();

#if UNITY_WEBGL && !UNITY_EDITOR
            //workaround for forcing Unity to save into idbfs
            PlayerPrefs.SetString("forceSave", string.Empty);
            PlayerPrefs.Save();
#endif
            _flushing = false;
            Flush();
        }

        protected abstract Task DoFlush();
        public string FilePath { get; protected set; }

        public void Dispose()
        {
            if (!_flushing)
            {
                Flush();
            }

            Disposed = true;
            _sessionId = null;
            _data.Clear();
            _cancellationTokenSource.Cancel();
        }
    }

    public interface ISavedPrefsData
    {
        public Dictionary<string, bool?> Booleans { get; set; }
        public Dictionary<string, int?> Integers { get; set; }
        public Dictionary<string, float?> Floats { get; set; }
        public Dictionary<string, string> Strings { get; set; }
        public Dictionary<string, byte[]> Objects { get; set; }

        void Clear();
    }
}