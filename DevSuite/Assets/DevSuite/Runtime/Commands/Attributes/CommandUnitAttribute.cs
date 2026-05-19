using System;
using System.Runtime.CompilerServices;

using Key =
#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Key;
#else
    UnityEngine.KeyCode;
#endif

namespace Ff.DevSuite.Commands.Attributes
{
    public abstract class CommandUnitAttribute : BaseDevSuiteAttribute
    {
        public string CommandId;
        public int Priority;
        public string Description;
        public bool SuppressExceptions = true;
        public float Flex = -1f;
        public string Color;
        public string FontResource;
        public int LineNumber;

        protected CommandUnitAttribute(string commandId, int lineNumber)
        {
            CommandId = commandId;
            LineNumber = lineNumber;
        }
    }

    [AttributeUsage(
        AttributeTargets.Property
        | AttributeTargets.Field
        | AttributeTargets.Method,
        AllowMultiple = true
    )]
    public class CommandValueAttribute : CommandUnitAttribute
    {
        private static float ValueNotSetWorkaround = float.MinValue;

        public string PossibleValuesFunctionName;
        public bool ReadOnly;
        public float MinValue = ValueNotSetWorkaround;
        public float MaxValue = ValueNotSetWorkaround;
        public bool ForceStringRepresentation;
        public ScaleType ScaleType;

        public CommandValueAttribute(string commandId = null, [CallerLineNumber] int lineNumber = 0) : base(commandId, lineNumber)
        {
            Priority = 0;
        }

        internal NumberRange<float>? ValuesRange => MinValue == ValueNotSetWorkaround || MaxValue == ValueNotSetWorkaround ? null : new NumberRange<float>(MinValue, MaxValue);
    }

    [AttributeUsage(
        AttributeTargets.Method
        | AttributeTargets.Event
    )]
    public class CommandButtonAttribute : CommandUnitAttribute
    {
        public string Title;
        public Key[] Shortcut;

        public CommandButtonAttribute(string commandId = null, [CallerLineNumber] int lineNumber = 0) : base(commandId, lineNumber)
        {
        }
    }
}