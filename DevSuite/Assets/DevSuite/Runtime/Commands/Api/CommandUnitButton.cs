using System;
using System.Collections.Generic;
using UnityEngine;
using Key =
#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Key;
#else
    UnityEngine.KeyCode;
#endif

namespace Ff.DevSuite.Commands
{
    internal class CommandUnitButton : BaseCommandUnit
    {
        public string Text { get; }
        public Action Action { get; }
        public Key[] Shortcut { get; }

        public CommandUnitButton(string text, Action action, float? priority = null, Key[] shortcut = null, string description = null, bool suppressExceptions = true, float flex = -1f, Color? color = null, string fontResource = null)
            : base(priority, description, suppressExceptions, flex, color, fontResource)
        {
            Text = text;
            Action = action;
            Shortcut = shortcut;
            if (Shortcut != null)
            {
                Array.Sort(Shortcut, (a, b) => -Comparer<Key>.Default.Compare(a, b));
            }
        }
    }
}