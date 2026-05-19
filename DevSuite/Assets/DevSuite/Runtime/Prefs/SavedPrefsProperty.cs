using System;

namespace Ff.Prefs
{
    public abstract class SavedPrefsProperty
    {
        public static Func<ISavedPrefs> Default { get; set; } = () => SavedPrefs.Default;
    }

    public class SavedPrefsProperty<T> : SavedPrefsProperty
    {
        private readonly string _keyName;
        private readonly T _defaultValue;
        private ISavedPrefs _savedPrefs;
        private Action<Touch> _onTouch;

        private StoredValue _storedValue;

        public bool Ready => _savedPrefs?.Ready ?? false;

        private string _sessionId;

        public SavedPrefsProperty(string keyName, T defaultValue = default, bool autoload = true, ISavedPrefs savedPrefs = null, Action<Touch> onTouch = null)
        {
            _keyName = keyName;
            _defaultValue = defaultValue;
            _onTouch = onTouch;

            if (autoload)
            {
                InitializeSavedPrefs(savedPrefs ?? Default?.Invoke());
            }
        }

        public static implicit operator T(SavedPrefsProperty<T> v) => v.Value;

        private void ReinitializeIfNeeded()
        {
            if (_sessionId == null || _sessionId != _savedPrefs.SessionId || _savedPrefs.Disposed)
            {
                InitializeSavedPrefs();
                _storedValue = default;
            }
        }

        public void InitializeSavedPrefs(ISavedPrefs savedPrefs = null)
        {
            savedPrefs ??= Default();
            _sessionId = savedPrefs.SessionId;
            if (savedPrefs == _savedPrefs)
                return;

            _savedPrefs = savedPrefs;
            if (_savedPrefs.Ready)
                return;

            _ = _savedPrefs.EnsureReady();
        }

        public T Value
        {
            get
            {
                ReinitializeIfNeeded();

                if (_storedValue.HasValue)
                {
                    _onTouch?.Invoke(new(this, _storedValue.Value, TouchType.Ping, _storedValue));
                    return _storedValue.Value;
                }

                var defaultValueCopy = _defaultValue;

                if (typeof(T) == typeof(bool?))
                {
                    var defaultValue = (bool?)(object)defaultValueCopy;
                    var val = _savedPrefs.GetBool(_keyName, defaultValue);
                    _storedValue = new((T)(object)val, true);
                }
                else if (typeof(T) == typeof(bool))
                {
                    var defaultValue = (bool)(object)defaultValueCopy;
                    var val = _savedPrefs.GetBool(_keyName, defaultValue) ?? false;
                    _storedValue = new((T)(object)val, true);
                }
                else if (typeof(T) == typeof(int?))
                {
                    var defaultValue = (int?)(object)defaultValueCopy;
                    var val = _savedPrefs.GetInt(_keyName, defaultValue);
                    _storedValue = new((T)(object)val, true);
                }
                else if (typeof(T) == typeof(int))
                {
                    var defaultValue = (int)(object)defaultValueCopy;
                    var val = _savedPrefs.GetInt(_keyName, defaultValue) ?? 0;
                    _storedValue = new((T)(object)val, true);
                }
                else if (typeof(T) == typeof(float?))
                {
                    var defaultValue = (float?)(object)defaultValueCopy;
                    var val = _savedPrefs.GetFloat(_keyName, defaultValue);
                    _storedValue = new((T)(object)val, true);
                }
                else if (typeof(T) == typeof(float))
                {
                    var defaultValue = (float)(object)defaultValueCopy;
                    var val = _savedPrefs.GetFloat(_keyName, defaultValue) ?? 0f;
                    _storedValue = new((T)(object)val, true);
                }
                else if (typeof(T) == typeof(string))
                {
                    var val = _savedPrefs.GetString(_keyName, _defaultValue as string);
                    _storedValue = new((T)(object)val, true);
                }
                else
                {
                    var val = _savedPrefs.GetObject(_keyName, _defaultValue);
                    _storedValue = new(val, true);
                }
                _onTouch?.Invoke(new(this, _storedValue.Value, TouchType.Changed, default));
                return _storedValue.Value;
            }
            set
            {
                ReinitializeIfNeeded();

                if (_storedValue.HasValue && (_storedValue.Value?.Equals(value) ?? false))
                {
                    _onTouch?.Invoke(new(this, _storedValue.Value, TouchType.Ping, _storedValue));
                    return;
                }

                var previousStoredValue = _storedValue;
                _storedValue = new(value, true);
                if (typeof(T) == typeof(bool?))
                {
                    var val = (bool?)(object)value;
                    _savedPrefs.SetBool(_keyName, val);
                }
                else if (typeof(T) == typeof(bool))
                {
                    var val = (bool)(object)value;
                    _savedPrefs.SetBool(_keyName, val);
                }
                else if (typeof(T) == typeof(int?))
                {
                    var val = (int?)(object)value;
                    _savedPrefs.SetInt(_keyName, val);
                }
                else if (typeof(T) == typeof(int))
                {
                    var val = (int)(object)value;
                    _savedPrefs.SetInt(_keyName, val);
                }
                else if (typeof(T) == typeof(float?))
                {
                    var val = (float?)(object)value;
                    _savedPrefs.SetFloat(_keyName, val);
                }
                else if (typeof(T) == typeof(float))
                {
                    var val = (float)(object)value;
                    _savedPrefs.SetFloat(_keyName, val);
                }
                else if (typeof(T) == typeof(string))
                {
                    var val = (string)(object)value;
                    _savedPrefs.SetString(_keyName, val);
                }
                else
                {
                    _savedPrefs.SetObject(_keyName, value);
                }
                _onTouch?.Invoke(new(this, _storedValue.Value, TouchType.Changed, previousStoredValue));
            }
        }

        public SavedPrefsProperty<T> ForceSave(bool flush = true)
        {
            if (!_storedValue.HasValue)
                return this;

            _storedValue = new(_storedValue.Value, false);
            Value = _storedValue.Value;
            if (flush)
                _savedPrefs.Flush();
            _onTouch?.Invoke(new(this, _storedValue.Value, TouchType.Saved, _storedValue));
            return this;
        }

        public struct Touch
        {
            private SavedPrefsProperty<T> Property { get; }
            public T Value { get; }
            public TouchType Type { get; }
            public StoredValue PreviousValue { get; }

            public Touch(SavedPrefsProperty<T> property, T value, TouchType type, StoredValue previousValue)
            {
                Property = property;
                Value = value;
                Type = type;
                PreviousValue = previousValue;
            }

            public void SetValue(T value)
            {
                Property.Value = value;
            }
        }

        public struct StoredValue
        {
            public T Value;
            public bool HasValue;

            public StoredValue(T value, bool hasValue)
            {
                Value = value;
                HasValue = hasValue;
            }
        }

        public enum TouchType
        {
            Ping,
            Changed,
            Saved,
        }
    }
}