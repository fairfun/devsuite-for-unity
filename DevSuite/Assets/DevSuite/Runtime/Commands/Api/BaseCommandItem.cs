using System;
using UnityEngine;

namespace Ff.DevSuite.Commands
{
    internal abstract class BaseCommandItem<T> : BaseCommandItem, IComparable<T> where T : BaseCommandItem<T>
    {
        protected BaseCommandItem(string id, float priority, Func<bool> visibility) :
            base(id, priority, visibility)
        { }

        public T WithDescription(string description)
        {
            Description = description;
            return (T)this;
        }

        public T WithColor(Color? color)
        {
            Color = color;
            return (T)this;
        }

        public T WithLineNumber(int lineNumber)
        {
            LineNumber = lineNumber;
            return (T)this;
        }

        public T WithDisplayName(string displayName)
        {
            if (!string.IsNullOrEmpty(displayName))
                DisplayName = displayName;
            return (T)this;
        }

        public int CompareTo(T other)
        {
            if (other == null)
                return -1;

            var cmp = GetNameSortOrder(Id).CompareTo(GetNameSortOrder(other.Id));
            if (cmp == 0)
                cmp = other.Priority.CompareTo(Priority);
            if (cmp == 0)
                cmp = LineNumber.CompareTo(other.LineNumber);
            if (cmp == 0)
                cmp = RegistrationOrder.CompareTo(other.RegistrationOrder);
            if (cmp == 0)
                cmp = string.Compare(Id, other.Id, StringComparison.Ordinal);
            return cmp;

            int GetNameSortOrder(string id)
            {
                if (id == DevSuiteContext.PinnedCategoryId || id == DevSuiteContext.PinnedMockId)
                    return 0;
                if (id == DevSuiteContext.DefaultGroupId)
                    return 1;
                return 2;
            }
        }
    }

    internal abstract class BaseCommandItem
    {
        public string Id { get; }
        public string Description { get; protected set; }
        public float Priority { get; }
        public int LineNumber { get; internal set; }
        public Func<bool> Visibility { get; }
        public int RegistrationOrder { get; set; }
        public Color? Color { get; set; }

        public TimeSpan? NextVisibilityCheckTime { get; private set; }
        public bool? LastVisibility { get; private set; }

        public string DisplayName { get; set; }

        protected BaseCommandItem(string id, float priority, Func<bool> visibility)
        {
            Id = id;
            Priority = priority;
            Visibility = visibility;
            DisplayName = DevSuiteUtils.TrimName(id);
        }

        public void UpdateVisibilityCheck(bool visible, TimeSpan nextCheck)
        {
            NextVisibilityCheckTime = nextCheck;
            LastVisibility = visible;
        }
    }
}