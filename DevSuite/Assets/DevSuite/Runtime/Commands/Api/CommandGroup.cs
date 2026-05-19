using System;

namespace Ff.DevSuite.Commands
{
    internal class CommandGroup : BaseCommandItem<CommandGroup>
    {
        public string CategoryId { get; }

        public CommandCategory AssignedToCategory { get; set; }
        public bool Collapsed { get; set; }

        public CommandGroup(string id, string categoryId, float priority, Func<bool> visibility) : base(
            id,
            priority,
            visibility
        )
        {
            CategoryId = categoryId;
        }

        public CommandGroup WithCollapsed(bool collapsed)
        {
            Collapsed = collapsed;
            return this;
        }

        private (object targetObject, string result)? _cachedFullName;

        public string GetFullName(object targetInstance)
        {
            if (_cachedFullName == null || _cachedFullName.Value.targetObject != targetInstance)
            {
                _cachedFullName = (targetInstance, DisplayName + (targetInstance != null ? $" ({targetInstance.GetType().Name}#{targetInstance.GetHashCode()})" : ""));
            }

            return _cachedFullName.Value.result;
        }
    }
}