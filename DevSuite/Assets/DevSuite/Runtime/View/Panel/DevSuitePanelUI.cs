using UnityEngine;
using UnityEngine.UIElements;
using System;

namespace Ff.DevSuite.View
{
    [RequireComponent(typeof(UIDocument))]
    internal class DevSuitePanelUI : MonoBehaviour
    {
        [Header("System")]
        [Tooltip("Auto initialization might be slow. Consider calling DevSuiteContext.Default.Initialize(<your assembly>) manually instead.")]
        [SerializeField] private bool _autoInitialize = true;

        [Header("Settings")]
        [SerializeField] private PanelSide _panelSide;
        [SerializeField] private LayoutMode _layoutMode;
        [SerializeField] private float _controlWidth = 350;
        [SerializeField] private float _commandsWidth = 500;
        [SerializeField, Range(0.2f, 5f)] private float _uiScale = 1f;
        [SerializeField] private Color _panelBackgroundColor = new Color(40 / 255f, 40 / 255f, 40 / 255f, 0.95f);
        [SerializeField] private Color _panelOutlineColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private DevSuitePanelActivationMode _activationMode = DevSuitePanelActivationMode.SingleClick;
        [SerializeField] private ControlPanelExpandButtonVisibility _expandButtonVisibility = ControlPanelExpandButtonVisibility.Visible;

        [Header("Main Layout")]
        [SerializeField] private VisualTreeAsset _layoutLandscapeRightUxml;
        [SerializeField] private VisualTreeAsset _layoutLandscapeLeftUxml;
        [SerializeField] private VisualTreeAsset _layoutPortraitUxml;
        [SerializeField] private StyleSheet _layoutUss;

        [Header("Control Panel")]
        [SerializeField] private VisualTreeAsset _controlUxml;
        [SerializeField] private StyleSheet _controlUss;

        [Header("Commands Panel")]
        [SerializeField] private VisualTreeAsset _commandsUxml;
        [SerializeField] private StyleSheet _commandsUss;

        [Header("Performance Panel")]
        [SerializeField] private VisualTreeAsset _performanceUxml;
        [SerializeField] private StyleSheet _performanceUss;

        [Header("Logs Panel")]
        [SerializeField] private VisualTreeAsset _logsUxml;
        [SerializeField] private StyleSheet _logsUss;

        private LogsPanelView _logsPanelView;
        private CommandsPanelView _commandsFullPanelView;
        private CommandsPanelView _commandsPinnedPanelView;
        private PerformancePanelView _performancePanelView;
        private ControlPanelView _controlView;

        private VisualElement _logsContainer;
        private VisualElement _commandsFullContainer;
        private VisualElement _basicContainer;
        private VisualElement _pinnedContainer;
        private VisualElement _performancePanelContainer;
        private VisualElement _controlContainer;

        private bool? _isPortrait;
        private bool IsPortrait =>_isPortrait ??= _layoutMode switch
        {
            LayoutMode.Auto => Screen.height > Screen.width,
            LayoutMode.Portrait => true,
            LayoutMode.Landscape => false,
            _ => throw new ArgumentOutOfRangeException()
        };

        private DevSuiteContext _context;

        private void Start()
        {
            if (_autoInitialize)
            {
                DevSuiteContext.Default.Initialize(this);
            }

            _context = DevSuiteContext.DefaultInternal;
            _context.OnChanged += UpdateVisibility;

            var uiDocument = GetComponent<UIDocument>();
            var root = uiDocument.rootVisualElement;
            root.Clear();

            ApplyScale(root);

            root.style.backgroundColor = StyleKeyword.Null; // Ensure it's not overridden
            root.RegisterCallback<AttachToPanelEvent>(_ => ApplyColors(root));

            var isLeft = _panelSide == PanelSide.Left;
            var layout = IsPortrait
                ? _layoutPortraitUxml
                : (isLeft ? _layoutLandscapeLeftUxml : _layoutLandscapeRightUxml);
            layout.CloneTree(root);

            root.styleSheets.Add(_layoutUss);

            _logsContainer = root.Q<VisualElement>("logs-container");
            _logsPanelView = new LogsPanelView(_logsUxml, _logsUss);
            _logsPanelView.AddToClassList("fill-container");
            _logsContainer.Add(_logsPanelView);
            _logsPanelView.Initialize(_context);

            _commandsFullContainer = root.Q<VisualElement>("commands-full-container");
            _commandsFullPanelView = new CommandsPanelView(_commandsUxml, _commandsUss);
            _commandsFullPanelView.AddToClassList("fill-container");
            _commandsFullContainer.Add(_commandsFullPanelView);
            _commandsFullPanelView.Initialize(_context, CommandsPanelView.ViewMode.Full);

            _basicContainer = root.Q<VisualElement>("basic-container");

            _controlContainer = root.Q<VisualElement>("control-container");
            _controlView = new ControlPanelView(_controlUxml, _controlUss, isLeft, _activationMode, _expandButtonVisibility);
            _controlView.Initialize(_context);
            _controlContainer.Add(_controlView);

            _pinnedContainer = root.Q<VisualElement>("pinned-commands-container");
            _commandsPinnedPanelView = new CommandsPanelView(_commandsUxml, _commandsUss);
            _commandsPinnedPanelView.AddToClassList("fill-container");
            _commandsPinnedPanelView.Initialize(_context, CommandsPanelView.ViewMode.Pinned);
            _pinnedContainer.Add(_commandsPinnedPanelView);

            _performancePanelContainer = root.Q<VisualElement>("performance-panel-container");
            _performancePanelView = new PerformancePanelView(_performanceUxml, _performanceUss);
            _performancePanelView.AddToClassList("fill-container");
            _performancePanelView.Initialize(_context);
            _performancePanelContainer.Add(_performancePanelView);

            ApplyColors(root);
            UpdateVisibility();
        }

        private void OnDestroy()
        {
            Reset();
        }

        public void Reset()
        {
            if (_context != null)
            {
                _context.OnChanged -= UpdateVisibility;
                _context = null;
            }

            _logsPanelView?.Reset();
            _commandsFullPanelView?.Reset();
            _commandsPinnedPanelView?.Reset();
            _performancePanelView?.Reset();
            _controlView?.Reset();
        }

        private void ApplyColors(VisualElement root)
        {
            if (_context == null)
                return;

            var isCollapsed = !_context.PanelExpanded;
            var panels = root.Query<VisualElement>(null, "ff-panel").ToList();
            foreach (var panel in panels)
            {
                panel.style.backgroundColor = isCollapsed ? StyleKeyword.Null : _panelBackgroundColor;
                panel.style.borderLeftColor = isCollapsed ? StyleKeyword.Null : _panelOutlineColor;
                panel.style.borderRightColor = isCollapsed ? StyleKeyword.Null : _panelOutlineColor;
                panel.style.borderTopColor = isCollapsed ? StyleKeyword.Null : _panelOutlineColor;
                panel.style.borderBottomColor = isCollapsed ? StyleKeyword.Null : _panelOutlineColor;
            }
        }

        private void ApplyScale(VisualElement root)
        {
            // Apply scale without touching the PanelSettings asset
            root.style.scale = new StyleScale(new Scale(new Vector3(_uiScale, _uiScale, 1f)));

            // Scale from the top-left corner
            root.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(0, 0, 0));

            // Adjust size so the scaled element still covers the full screen
            float percent = 100f / _uiScale;
            root.style.width = Length.Percent(percent);
            root.style.height = Length.Percent(percent);
        }

        private void OnValidate()
        {
            var uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                ApplyScale(uiDocument.rootVisualElement);
                ApplyColors(uiDocument.rootVisualElement);
            }
        }

        private void UpdateVisibility()
        {
            var expanded = _context.PanelExpanded;

            if (IsPortrait)
            {
                _basicContainer.style.width = Length.Percent(100);
            }
            else
            {
                _basicContainer.style.width = expanded ? _controlWidth : StyleKeyword.Null;
            }

            _logsContainer.style.display = expanded && _context.LogsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _commandsFullContainer.style.display = expanded && _context.CommandsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _pinnedContainer.style.display = expanded && _context.PinnedCommandsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _performancePanelContainer.style.display = expanded && _context.MetricsVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (IsPortrait)
            {
                _commandsFullContainer.style.width = StyleKeyword.Auto;
                _commandsFullContainer.style.flexGrow = 1;
                _commandsFullContainer.style.flexShrink = 1;
                _commandsFullContainer.style.flexBasis = 0;

                _logsContainer.style.flexGrow = 1;
                _logsContainer.style.flexShrink = 1;
                _logsContainer.style.flexBasis = 0;

                _commandsFullContainer.style.alignSelf = StyleKeyword.Null;
                _commandsFullContainer.style.maxHeight = StyleKeyword.Null;
                _logsContainer.style.alignSelf = StyleKeyword.Null;
                _logsContainer.style.maxHeight = StyleKeyword.Null;
            }
            else
            {
                _commandsFullContainer.style.width = expanded ? _commandsWidth : StyleKeyword.Null;
                _commandsFullContainer.style.flexGrow = StyleKeyword.Null;
                _commandsFullContainer.style.flexShrink = StyleKeyword.Null;
                _commandsFullContainer.style.flexBasis = StyleKeyword.Null;

                _logsContainer.style.width = StyleKeyword.Null;
                _logsContainer.style.flexGrow = StyleKeyword.Null;
                _logsContainer.style.flexShrink = StyleKeyword.Null;
                _logsContainer.style.flexBasis = StyleKeyword.Null;

                _commandsFullContainer.style.alignSelf = Align.FlexStart;
                _commandsFullContainer.style.maxHeight = Length.Percent(100);
                _logsContainer.style.alignSelf = Align.FlexStart;
                _logsContainer.style.maxHeight = Length.Percent(100);
            }

            foreach (var child in _basicContainer.Children())
            {
                child.RemoveFromClassList("basic-container-element--last");
            }

            for (int i = _basicContainer.childCount - 1; i >= 0; i--)
            {
                var child = _basicContainer[i];
                if (child.style.display != DisplayStyle.None)
                {
                    child.AddToClassList("basic-container-element--last");
                    break;
                }
            }

            ApplyColors(GetComponent<UIDocument>().rootVisualElement);
        }

        internal enum LayoutMode
        {
            Auto,
            Landscape,
            Portrait,
        }

        internal enum PanelSide
        {
            Right,
            Left,
        }
    }
}