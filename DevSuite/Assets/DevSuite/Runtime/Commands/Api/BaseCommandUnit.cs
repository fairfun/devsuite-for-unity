using System;
using UnityEngine;

namespace Ff.DevSuite.Commands
{
    internal abstract class BaseCommandUnit : IComparable<BaseCommandUnit>
    {
        private const float DefaultFlex = 1f;

        public float Priority { get; set; }
        public int LineNumber { get; set; }
        public string Description { get; set; }
        public bool SuppressExceptions { get; }
        public float Flex { get; }
        public Color? Color { get; set; }
        public string FontResource { get; }

        public int RegistrationOrder { get; set; }
        public Command AssignedToCommand { get; set; }

        protected BaseCommandUnit(float? priority, string description, bool suppressExceptions, float flex, Color? color = null, string fontResource = null)
        {
            Priority = priority ?? 0;
            Description = description;
            SuppressExceptions = suppressExceptions;
            Flex = flex < 0 ? DefaultFlex : flex;
            Color = color;
            FontResource = fontResource;
        }

        public BaseCommandUnit WithLineNumber(int lineNumber)
        {
            LineNumber = lineNumber;
            return this;
        }

        public int CompareTo(BaseCommandUnit other)
        {
            if (ReferenceEquals(this, other))
                return 0;
            var cmp = other == null ? -1 : 0;
            if (cmp == 0)
                cmp = -Priority.CompareTo(other.Priority);
            if (cmp == 0)
                cmp = LineNumber.CompareTo(other.LineNumber);
            if (cmp == 0)
                cmp = (this is CommandUnitButton ? 1 : 0).CompareTo(other is CommandUnitButton ? 1 : 0);
            if (cmp == 0)
                cmp = RegistrationOrder.CompareTo(other.RegistrationOrder);
            return cmp;
        }
    }
}