using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ff.DevSuite
{
    internal class NumberCounter<T> where T : struct
    {
        private int _length;
        private Queue<T> _values = new();

        static NumberCounter()
        {
            if (_add == null)
            {
                Defaults.Init();
            }
        }

        public NumberCounter(int length)
        {
            _length = length;
        }

        public NumberCounter<T> Add(T value)
        {
            _values.Enqueue(value);
            while (_values.Count > _length)
            {
                _values.Dequeue();
            }

            return this;
        }

        public T AverageOrDefault()
        {
            if (_values.Count <= 0)
            {
                return _default();
            }

            var result = _default();
            foreach (var value in _values)
            {
                result = _add(result, value);
            }

            return _divide(result, _values.Count);
        }

        public T MinOrDefault()
        {
            if (_values.Count <= 0)
                return _default();

            var min = _values.Peek();
            var comparer = Comparer<T>.Default;
            foreach (var value in _values)
            {
                if (comparer.Compare(value, min) < 0)
                    min = value;
            }
            return min;
        }

        public T MaxOrDefault()
        {
            if (_values.Count <= 0) return
                _default();

            var max = _values.Peek();
            var comparer = Comparer<T>.Default;
            foreach (var value in _values)
            {
                if (comparer.Compare(value, max) > 0)
                    max = value;
            }
            return max;
        }

        public bool IsFilled => _values.Count >= _length;

        private static Func<T, T, T> _add;
        private static Func<T, int, T> _divide;
        private static Func<T> _default;

        public static void Register(Func<T> @default, Func<T, T, T> add, Func<T, int, T> divide)
        {
            _default = @default;
            _add = add;
            _divide = divide;
        }

        internal static class Defaults
        {
            public static void Init()
            {
                NumberCounter<int>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
                NumberCounter<uint>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / (uint)b
                );
                NumberCounter<long>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
                NumberCounter<ulong>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / (ulong)b
                );
                NumberCounter<float>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
                NumberCounter<double>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
                NumberCounter<decimal>.Register(
                    () => 0,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
                NumberCounter<Vector3>.Register(
                    () => Vector3.zero,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
                NumberCounter<Vector2>.Register(
                    () => Vector2.zero,
                    (a, b) => a + b,
                    (a, b) => a / b
                );
            }
        }
    }
}