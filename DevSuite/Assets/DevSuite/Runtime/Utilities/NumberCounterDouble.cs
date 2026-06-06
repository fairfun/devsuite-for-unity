using System;

namespace Ff.DevSuite
{
    internal class NumberCounterDouble
    {
        internal struct Stats
        {
            public double Average;
            public double Min;
            public double Max;
        }

        private readonly double[] _buffer;
        private int _head;
        private int _count;
        private double _sum;
        private double _min;
        private double _max;

        public NumberCounterDouble(int length)
        {
            _buffer = new double[length];
        }

        public bool IsFilled => _count >= _buffer.Length;

        public NumberCounterDouble Add(double value)
        {
            var length = _buffer.Length;

            if (_count < length)
            {
                _buffer[_count] = value;
                _count++;

                _sum += value;
                if (_count == 1)
                {
                    _min = _max = value;
                }
                else
                {
                    if (value < _min)
                        _min = value;
                    if (value > _max)
                        _max = value;
                }
            }
            else
            {
                var oldValue = _buffer[_head];
                _buffer[_head] = value;
                _sum += value - oldValue;

                if (oldValue == _min || oldValue == _max)
                {
                    _min = _max = _buffer[0];
                    for (var i = 1; i < length; i++)
                    {
                        var v = _buffer[i];
                        if (v < _min)
                            _min = v;
                        if (v > _max)
                            _max = v;
                    }
                }

                if (value < _min)
                    _min = value;
                if (value > _max)
                    _max = value;

                _head = (_head + 1) % length;
            }

            return this;
        }

        public Stats GetStats()
        {
            if (_count <= 0)
                return new Stats();
            return new Stats { Average = _sum / _count, Min = _min, Max = _max };
        }
    }
}
