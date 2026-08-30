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
        public BaseCommandUnit OwnerUnit { get; set; }

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
            if (other == null)
                return -1;

            var primaryThis = OwnerUnit ?? this;
            var primaryOther = other.OwnerUnit ?? other;

            var cmp = -primaryThis.Priority.CompareTo(primaryOther.Priority);
            if (cmp == 0)
                cmp = primaryThis.LineNumber.CompareTo(primaryOther.LineNumber);

            if (cmp == 0 && !ReferenceEquals(primaryThis, primaryOther))
            {
                cmp = (primaryThis is CommandUnitButton ? 1 : 0).CompareTo(primaryOther is CommandUnitButton ? 1 : 0);
                if (cmp == 0)
                    cmp = primaryThis.RegistrationOrder.CompareTo(primaryOther.RegistrationOrder);
            }

            if (cmp == 0)
            {
                cmp = (this is CommandUnitButtonParameter ? 1 : 0).CompareTo(other is CommandUnitButtonParameter ? 1 : 0);
                if (cmp == 0 && this is CommandUnitButtonParameter paramThis && other is CommandUnitButtonParameter paramOther)
                {
                    cmp = paramThis.ParameterIndex.CompareTo(paramOther.ParameterIndex);
                }
            }

            if (cmp == 0)
                cmp = RegistrationOrder.CompareTo(other.RegistrationOrder);

            return cmp;
        }
    }
}