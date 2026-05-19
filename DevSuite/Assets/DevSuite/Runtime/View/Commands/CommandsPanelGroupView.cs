using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class CommandsPanelGroupView : VisualElement
    {
        private Label _header;
        private Label _description;
        private Label _toggleLabel;
        public VisualElement Content { get; private set; }
        private float _labelDefaultAlpha;

        internal CommandsPanelGroupView(
            DevSuiteContext context,
            string categoryId,
            string groupId,
            string name,
            CommandsPanelView.ViewMode mode,
            string description = null,
            Color? color = null,
            bool defaultCollapsed = false
        )
        {
            AddToClassList("ff-commands-group");

            Content = new VisualElement();

            if (mode == CommandsPanelView.ViewMode.Full)
            {
                var headerContainer = new VisualElement();
                headerContainer.AddToClassList("ff-commands-group-header-container");
                Add(headerContainer);

                var isCollapsed = context.IsGroupCollapsed(groupId, categoryId, defaultCollapsed);

                _toggleLabel = new Label();
                _toggleLabel.AddToClassList("ff-commands-group-toggle-indicator");
                headerContainer.Add(_toggleLabel);

                _header = new Label(name);
                _header.AddToClassList("ff-commands-group-header");
                _header.style.color = color ?? new Color(1f, 200f / 255f, 0f);
                _labelDefaultAlpha = _header.style.color.value.a;
                headerContainer.Add(_header);

                if (!string.IsNullOrEmpty(description))
                {
                    _description = new Label(description);
                    _description.AddToClassList("ff-commands-group-description");
                    headerContainer.Add(_description);
                }

                SetCollapsed(isCollapsed);

                headerContainer.RegisterCallback<ClickEvent>(evt =>
                {
                    isCollapsed = !isCollapsed;
                    SetCollapsed(isCollapsed);
                    context.ToggleGroupCollapse(groupId, categoryId, isCollapsed);
                });
            }
            else
            {
                style.borderTopWidth = style.borderRightWidth =
                    style.borderBottomWidth = style.borderLeftWidth = 0;
            }

            Add(Content);
        }

        private void SetCollapsed(bool collapsed)
        {
            _toggleLabel.text = collapsed ? "\uf0fe" : "\uf146";
            Content.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;

            var labelColor = _header.style.color.value;
            labelColor.a = _labelDefaultAlpha * (collapsed ? 0.3f : 1f);
            _header.style.color = labelColor;
            _toggleLabel.style.color = labelColor;
        }
    }
}