using Ff.DevSuite.Commands;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class CommandsPanelCommandView : VisualElement
    {
        private Label _label;
        private Button _icon;
        public VisualElement UnitsContainer { get; private set; }

        public CommandsPanelCommandView(Command command, DevSuiteContext context = null, System.Action<VisualElement, string> registerTooltip = null)
        {
            AddToClassList("ff-commands-command-row");

            var isAlwaysPinned = command.AlwaysPin;
            var isPinned = context?.GetPinnedCommands(false).Contains(command) ?? false;

            string icon;
            string pinTooltip;
            if (isAlwaysPinned)
            {
                icon = "\uf023";
                pinTooltip = "Always Pinned";
            }
            else if (isPinned)
            {
                icon = "\uf08d";
                pinTooltip = "Pinned. Click to unpin";
            }
            else
            {
                icon = "\ue68f";
                pinTooltip = "Unpinned. Click to pin";
            }

            _icon = new Button { text = icon };
            _icon.AddToClassList("ff-commands-command-icon");
            _icon.clicked += () =>
            {
                if (context != null && !isAlwaysPinned)
                {
                    context.TogglePinItem(command, !isPinned);
                }
            };
            registerTooltip?.Invoke(_icon, pinTooltip);
            Add(_icon);

            var copyIcon = new Button
            {
                text = "\uf0c5"
            };
            copyIcon.AddToClassList("ff-commands-command-icon");
            copyIcon.clicked += () =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Category: {command.CategoryId ?? DevSuiteContext.DefaultGroupId}");
                sb.AppendLine($"  Group: {command.GroupId ?? DevSuiteContext.DefaultGroupId}");
                sb.AppendLine($"    Command: {command.Id ?? DevSuiteContext.DefaultGroupId}");
                var i = 0;
                foreach (var unit in command.Units)
                {
                    var unitPrefix = $"      Unit{i++}: ";
                    if (unit is CommandUnitValue dial)
                    {
                        var strVal = context.GetRepresentation<string>(dial, out _) ?? DevSuiteContext.NullRepresentation;
                        strVal = string.Join($"\n{new string(' ', unitPrefix.Length)}", DevSuiteUtils.NewLineRegex.Split(strVal));
                        sb.AppendLine($"{unitPrefix}{strVal}");
                    }
                    else if (unit is CommandUnitButton button)
                    {
                        sb.AppendLine($"{unitPrefix}button '{button.Text}'");
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
                DevSuiteUtils.CopyToClipboard(sb.ToString());

                DevSuiteUtils.ShowIconButtonClickedFeedback(copyIcon);
            };
            registerTooltip?.Invoke(copyIcon, "Copy Values");
            Add(copyIcon);

            _label = new Label(command.DisplayName);
            _label.AddToClassList("ff-commands-command-label");
            if (command.Color != null)
            {
                _label.style.color = command.Color.Value;
            }
            registerTooltip?.Invoke(_label, command.Description);
            Add(_label);

            UnitsContainer = new VisualElement();
            UnitsContainer.AddToClassList("ff-commands-units-container");
            Add(UnitsContainer);

            if (command.HeightMultiplier > 1)
            {
                style.height = 26f * command.HeightMultiplier;
                AddToClassList("ff-commands-command-row--custom-height");
                _label.style.alignSelf = Align.Stretch;
                _label.style.unityTextAlign = TextAnchor.MiddleLeft;
                _label.style.whiteSpace = WhiteSpace.Normal;
            }
        }
    }
}