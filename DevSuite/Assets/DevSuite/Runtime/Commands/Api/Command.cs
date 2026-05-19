using System;
using System.Collections.Generic;

namespace Ff.DevSuite.Commands
{
    internal class Command : BaseCommandItem<Command>
    {
        public string CategoryId { get; }
        public string GroupId { get; }
        public object TargetInstance { get; }
        public List<BaseCommandUnit> Units { get; }
        public float HeightMultiplier { get; }
        public bool AlwaysPin { get; }

        public CommandGroup AssignedToGroup { get; set; }

        public Command(string id,
            string groupId,
            string categoryId,
            float priority,
            Func<bool> visibility,
            object targetInstance,
            List<BaseCommandUnit> units,
            float heightMultiplier = -1f,
            bool alwaysPin = false)
                : base(
                    id,
                    priority,
                    visibility
                )
        {
            CategoryId = categoryId;
            GroupId = groupId;
            TargetInstance = targetInstance;
            Units = units ?? new List<BaseCommandUnit>();
            HeightMultiplier = heightMultiplier;
            AlwaysPin = alwaysPin;
        }

        public Command With(BaseCommandUnit unit)
        {
            Units.Add(unit);
            return this;
        }
    }
}