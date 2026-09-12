using System;

namespace Ff.DevSuite.Commands
{
    public class CommandCategory : BaseCommandItem<CommandCategory>
    {
        public CommandCategory(string id, float priority, Func<bool> visibility) : base(id, priority, visibility) { }
    }
}