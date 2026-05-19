using System.Runtime.CompilerServices;

namespace Ff.DevSuite.Commands.Attributes
{
    public class CommandAttribute : BaseCommandAttribute
    {
        public string GroupId;
        public string CategoryId;
        public float HeightMultiplier = -1f;
        public string CommandId;
        public bool AlwaysPin;

        public CommandAttribute(string id = null, string commandId = null, [CallerLineNumber] int lineNumber = 0) : base(id, lineNumber)
        {
            CommandId = commandId;
        }
    }
}