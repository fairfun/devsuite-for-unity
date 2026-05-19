using System;
using System.Collections.Generic;

namespace Ff.DevSuite
{
    internal class LazyCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _values = new();
        private readonly Func<TKey, TValue> _getValue;

        private Func<ICollection<TKey>> _warmupKeys;
        private bool _warmedup;

        private Action _populateFunction;
        private bool _populated;

        public LazyCache(Func<TKey, TValue> getValue)
        {
            _getValue = getValue;
        }

        public LazyCache<TKey, TValue> WithWarmup(Func<ICollection<TKey>> keys, bool now = false)
        {
            _warmedup = false;
            _warmupKeys = keys;

            if (now)
            {
                WarmupIfNeeded();
            }

            return this;
        }

        public LazyCache<TKey, TValue> WithPopulate<TPopulate>(Func<IReadOnlyCollection<TPopulate>> source, Func<TPopulate, TKey> key, Func<TPopulate, TValue> value, bool now = false)
        {
            _populated = false;
            _populateFunction = () =>
            {
                foreach (var src in source.Invoke())
                {
                    _values[key(src)] = value(src);
                }
            };

            if (now)
            {
                PopulateIfNeeded();
            }
            return this;
        }

        private void WarmupIfNeeded()
        {
            if (_warmupKeys == null || _warmedup)
                return;

            foreach (var key in _warmupKeys.Invoke())
            {
                DoGet(key);
            }
            _warmedup = true;
        }

        private void PopulateIfNeeded()
        {
            if (_populateFunction == null || _populated)
                return;

            _populateFunction.Invoke();
            _populated = true;
        }

        public TValue this[TKey key] => Get(key);

        public TValue Get(TKey key)
        {
            PopulateIfNeeded();
            WarmupIfNeeded();

            return DoGet(key);
        }

        protected TValue DoGet(TKey key)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                _values[key] = value = _getValue.Invoke(key);
            }
            return value;
        }

        public void Clear(bool full = false)
        {
            _values.Clear();
            _warmedup = false;
            _populated = false;

            if (full)
            {
                _warmupKeys = null;
                _populateFunction = null;
            }
        }
    }

    internal class LazyCache<TKey1, TKey2, TValue> : LazyCache<(TKey1, TKey2), TValue>
    {
        public LazyCache(Func<TKey1, TKey2, TValue> getValue) : base((keys) => getValue(keys.Item1, keys.Item2)) { }
        public TValue this[TKey1 key1, TKey2 key2] => Get(key1, key2);

        public TValue Get(TKey1 key1, TKey2 key2)
        {
            return Get((key1, key2));
        }
    }

    internal class LazyCache<TKey1, TKey2, TKey3, TValue> : LazyCache<(TKey1, TKey2, TKey3), TValue>
    {
        public LazyCache(Func<TKey1, TKey2, TKey3, TValue> getValue) : base((keys) => getValue(keys.Item1, keys.Item2, keys.Item3)) { }
        public TValue this[TKey1 key1, TKey2 key2, TKey3 key3] => Get(key1, key2, key3);

        public TValue Get(TKey1 key1, TKey2 key2, TKey3 key3)
        {
            return Get((key1, key2, key3));
        }
    }

    internal class LazyCache<TKey1, TKey2, TKey3, TKey4, TValue> : LazyCache<(TKey1, TKey2, TKey3, TKey4), TValue>
    {
        public LazyCache(Func<TKey1, TKey2, TKey3, TKey4, TValue> getValue) : base((keys) => getValue(keys.Item1, keys.Item2, keys.Item3, keys.Item4)) { }
        public TValue this[TKey1 key1, TKey2 key2, TKey3 key3, TKey4 key4] => Get(key1, key2, key3, key4);

        public TValue Get(TKey1 key1, TKey2 key2, TKey3 key3, TKey4 key4)
        {
            return Get((key1, key2, key3, key4));
        }
    }
}