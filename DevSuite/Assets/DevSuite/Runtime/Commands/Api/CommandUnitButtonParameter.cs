using System;
using System.Collections;
using UnityEngine;

namespace Ff.DevSuite.Commands
{
    internal class CommandUnitButtonParameter : CommandUnitValue
    {
        public CommandUnitButton OwnerButton { get; }
        public int ParameterIndex { get; }
        public string ParameterName { get; }

        public CommandUnitButtonParameter(
            CommandUnitButton ownerButton,
            int parameterIndex,
            string parameterName,
            Type type,
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
            string fontResource = null,
            string format = null)
            : base(type, getValue, saveValue, allowedValues, valuesRange, priority, forceStringRepresentation, description, scaleType, suppressExceptions, flex, color, fontResource, format)
        {
            OwnerButton = ownerButton;
            OwnerUnit = ownerButton;
            ParameterIndex = parameterIndex;
            ParameterName = parameterName;
        }
    }
}
