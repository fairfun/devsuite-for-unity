using System;
using System.Collections;
using System.Collections.Generic;

namespace Ff.DevSuite.Commands
{
    public class CommandValuesProvider
    {
        public Type Type { get; }
        public GetValues Values { get; }

        public CommandValuesProvider(Type type, GetValues values)
        {
            Type = type;
            Values = values;
        }

        public delegate IEnumerable GetValues(Type type);
    }

    internal static class DefaultCommandValuesProviders
    {
        public static IReadOnlyList<CommandValuesProvider> Get()
        {
            return new[]
            {
                new CommandValuesProvider(typeof(Enum), Enum.GetValues)
            };
        }
    }
}