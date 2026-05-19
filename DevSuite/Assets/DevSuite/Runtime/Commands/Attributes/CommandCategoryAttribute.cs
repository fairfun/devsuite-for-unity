using System.Runtime.CompilerServices;

namespace Ff.DevSuite.Commands.Attributes
{
    public class CommandCategoryAttribute : BaseCommandAttribute
    {
        public CommandCategoryAttribute(string id = null, [CallerLineNumber] int lineNumber = 0) : base(id, lineNumber)
        {
        }
    }
}