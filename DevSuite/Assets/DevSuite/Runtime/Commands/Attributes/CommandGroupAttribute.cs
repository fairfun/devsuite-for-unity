using System.Runtime.CompilerServices;

namespace Ff.DevSuite.Commands.Attributes
{
    public class CommandGroupAttribute : BaseCommandAttribute
    {
        public string CategoryId;
        public bool Collapsed;

        public CommandGroupAttribute(string id = null, [CallerLineNumber] int lineNumber = 0) : base(id, lineNumber)
        {
        }
    }
}