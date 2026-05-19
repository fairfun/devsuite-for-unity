using System;
using System.Collections.Generic;

namespace Ff.DevSuite.Commands
{
    public class CommandFunctionsSourceProvider
    {
        public Type Type { get; }
        public object TargetInstance { get; }
        public ISet<string> FunctionsNames { get; }

        public CommandFunctionsSourceProvider(Type type, ISet<string> functionsNames = null)
        {
            Type = type;
            FunctionsNames = functionsNames;
        }

        public CommandFunctionsSourceProvider(object targetInstance = null, ISet<string> functionsNames = null)
            : this(targetInstance.GetType(), functionsNames)
        {
            TargetInstance = targetInstance;
        }
    }
}