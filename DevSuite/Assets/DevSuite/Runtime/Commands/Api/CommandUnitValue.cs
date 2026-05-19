using System;
using System.Collections;
using UnityEngine;

namespace Ff.DevSuite.Commands
{
    internal class CommandUnitValue : BaseCommandUnit
    {
        internal Type Type { get; }
        internal Func<object> GetValue { get; }
        internal Action<object> SaveValue { get; }
        internal Func<IEnumerable> AllowedValues { get; }
        internal NumberRange<float>? ValuesRange { get; set; }
        internal bool ForceStringRepresentation { get; }
        internal ScaleType ScaleType { get; set; }

        public CommandUnitValue(Type type,
            Func<object> getValue,
            Action<object> saveValue = null,
            Func<IEnumerable> allowedValues = null,
            (float, float)? valuesRange = null,
            float? priority = null,
            bool forceStringRepresentation = false,
            string description = null,
            ScaleType scaleType = default,
            bool suppressExceptions = true,
            float flex = -1f,
            Color? color = null,
            string fontResource = null)
                : base(priority, description, suppressExceptions, flex, color, fontResource)
        {
            Type = type;
            GetValue = getValue;
            SaveValue = saveValue;
            AllowedValues = allowedValues;
            ValuesRange = valuesRange == null ? null : new NumberRange<float>(valuesRange.Value.Item1, valuesRange.Value.Item2);
            ForceStringRepresentation = forceStringRepresentation;
            ScaleType = scaleType;
        }

        public bool ReadOnly => SaveValue == null;

        internal float FromSliderToValue(float sliderValue)
        {
            float result;

            switch (ScaleType)
            {
                case ScaleType.Linear:
                    result = ValuesRange.Value.Min + ValuesRange.Value.Length() * Linear(sliderValue);
                    break;

                case ScaleType.Logarithmic:
                    result = ValuesRange.Value.Min + ValuesRange.Value.Length() * LogarithmicDirect(sliderValue);
                    break;

                default:
                    throw new Exception($"Unexpected {nameof(ScaleType)} '{ScaleType}'");
            }

            return result;
        }

        internal float FromValueToSlider(float value)
        {
            float result;
            var rel = (value - ValuesRange.Value.Min) / ValuesRange.Value.Length();

            switch (ScaleType)
            {
                case ScaleType.Linear:
                    result = Linear(rel);
                    break;

                case ScaleType.Logarithmic:
                    result = LogarithmicReverse(rel);
                    break;

                default:
                    throw new Exception($"Unexpected {nameof(ScaleType)} '{ScaleType}'");
            }
            return result;
        }

        private float Linear(float value)
        {
            return value;
        }

        private float LogarithmicDirect(float value)
        {
            var span = ValuesRange.Value.Length();
            return (float)(Math.Pow(span, value) - 1f) / (span - 1f);
        }

        private float LogarithmicReverse(float value)
        {
            var span = ValuesRange.Value.Length();
            return (float)Math.Log(value * (span - 1f) + 1f, span);
        }
    }

    public enum ScaleType
    {
        Linear,
        Logarithmic,
    }
}