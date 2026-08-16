using System;

namespace Ff.DevSuite.Performance
{
    public abstract class BaseGraphDataProvider : IDisposable
    {
        public const int CounterLength = 100;

        internal NumberCounterDouble _counter = new(CounterLength);
        public virtual float? ReferenceValueColorImpact => 1.5f;
        internal event Action<DataPoint> OnUpdate;
        internal abstract string Label { get; }
        internal abstract string UnitName { get; }

        public void Process()
        {
            var currentValue = GetCurrentValue();
            _counter.Add(currentValue);
            var stats = _counter.GetStats();
            OnUpdate?.Invoke(new DataPoint(currentValue, GetReferenceValue(), stats.Average, stats.Min, stats.Max, ReferenceValueColorImpact));
        }

        public virtual void Dispose()
        {
        }

        protected abstract double GetCurrentValue();
        public GraphDataProviderSettings Settings { get; set; } = new();

        protected virtual double? GetReferenceValue()
        {
            return Settings?.ReferenceValueProvider?.Invoke();
        }

        internal struct DataPoint
        {
            public double CurrentValue;
            public double? ReferenceValue;
            public double AverageValue;
            public double MinValue;
            public double MaxValue;
            public float? ReferenceColorImpact;

            public DataPoint(double currentValue, double? referenceValue, double averageValue, double minValue, double maxValue, float? referenceColorImpact)
            {
                CurrentValue = currentValue;
                ReferenceValue = referenceValue;
                AverageValue = averageValue;
                MinValue = minValue;
                MaxValue = maxValue;
                ReferenceColorImpact = referenceColorImpact;
            }
        }
    }

    public class GraphDataProviderSettings
    {
        public Func<double?> ReferenceValueProvider { get; set; }
        public bool? ExpandedByDefault { get; set; }
        public bool? Register { get; set; }

        public GraphDataProviderSettings()
        {
        }

        public GraphDataProviderSettings(Func<double?> referenceValueProvider = null, bool? expandedByDefault = null, bool? register = null)
        {
            ReferenceValueProvider = referenceValueProvider;
            ExpandedByDefault = expandedByDefault;
            Register = register;
        }

        public GraphDataProviderSettings(bool expandedByDefault, bool register = true)
        {
            ExpandedByDefault = expandedByDefault;
            Register = register;
        }
    }
}