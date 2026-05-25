using System;
using System.Collections.Generic;

namespace Ff.DevSuite
{
    internal class ValueStack<T>
    {
        public event Action<T> OnChanged;

        private readonly SortedList<int, ValueStackValue> _values = new();

        public ValueStack(T defaultValue = default, int defaultPriority = 0)
        {
            _values.Add(defaultPriority, new ValueStackValue(defaultValue));
        }

        public IDisposable SetAndTrack(T value, int priority, object requestor)
        {
            var res = new DisposeAction(() => Remove(priority, requestor));
            Set(value, priority, requestor);
            return res;
        }

        public void Set(T value, int priority, object requestor)
        {
            var oldValue = Value;
            if (!_values.TryGetValue(priority, out var val))
            {
                val = new(value);
            }
            if ((val.Value == null) != (value == null) ||
                val.Value != null && !val.Value.Equals(value))
            {
                throw new ArgumentException($"Specified value differs from the previously saved value for priority '{priority}'");
            }
            val.Requestors.Add(requestor);
            _values[priority] = val;
            if (HasChanged(oldValue, Value))
            {
                DispatchChanged();
            }
        }

        public void Remove(int priority, object requestor)
        {
            var oldValue = Value;
            if (!_values.TryGetValue(priority, out var val))
            {
                return;
            }

            val.Requestors.Remove(requestor);
            if (val.Requestors.Count <= 0)
            {
                _values.Remove(priority);
                if (HasChanged(Value, oldValue))
                {
                    DispatchChanged();
                }
            }
        }

        private static bool HasChanged(T newValue, T oldValue)
        {
            return newValue == null && oldValue != null || newValue != null && !newValue.Equals(oldValue);
        }

        public void Toggle(T value, int priority, bool toSet, object requestor)
        {
            if (toSet)
            {
                Set(value, priority, requestor);
            }
            else
            {
                Remove(priority, requestor);
            }
        }

        public T Value => _values.Count <= 0 ? default : _values.Values[^1].Value;

        private void DispatchChanged()
        {
            OnChanged?.Invoke(Value);
        }

        public static implicit operator T(ValueStack<T> d) => d.Value;

        private class ValueStackValue
        {
            public HashSet<object> Requestors { get; } = new();
            public T Value;

            public ValueStackValue(T value)
            {
                Value = value;
            }
        }

        public ValueStack<T> SetChangeHandler(T value, Action action)
        {
            OnChanged += v =>
            {
                if (v == null && value == null ||
                    v != null && v.Equals(value))
                {
                    action.Invoke();
                }
            };
            return this;
        }
    }
}