namespace Ff.DevSuite
{
    internal readonly struct NumberRange<T>
    {
        public T Min { get; }
        public T Max { get; }

        public NumberRange(T min, T max)
        {
            Min = min;
            Max = max;
        }

        public static implicit operator NumberRange<T>((T, T) v) => new(v.Item1, v.Item2);
    }
}