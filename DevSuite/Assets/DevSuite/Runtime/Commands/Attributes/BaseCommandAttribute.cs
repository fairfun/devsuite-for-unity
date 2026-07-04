using System;
using System.Runtime.CompilerServices;

namespace Ff.DevSuite.Commands.Attributes
{
    [AttributeUsage(
        AttributeTargets.Class
        | AttributeTargets.Field
        | AttributeTargets.Method
        | AttributeTargets.Property
        | AttributeTargets.Struct
        | AttributeTargets.Event
    )]
    public abstract class BaseCommandAttribute : BaseDevSuiteAttribute
    {
        public string Id;
        public string VisibilityFunctionName;
        public string Description;
        public string DisplayName;
        public int Priority;
        public string Color;
        public readonly int LineNumber;
        public AttributeScope Scope;

        protected BaseCommandAttribute(string id, [CallerLineNumber] int lineNumber = 0)
        {
            Id = id;
            LineNumber = lineNumber;
        }
    }

    public abstract class BaseDevSuiteAttribute : Attribute { }

    public enum AttributeScope
    {
        Current,
        Continuous,
    }
}