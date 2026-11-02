using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using UnityEngine.Serialization;
using Ff.DevSuite.Performance;

namespace Ff.DevSuite.View
{
    [DefaultExecutionOrder(-99)]
    [RequireComponent(typeof(UIDocument))]
    internal class DevSuitePanelUI : MonoBehaviour
    {
        [Header("System")]
        [Tooltip("Auto initialization might be slow. Consider calling DevSuiteContext.Default.Initialize(<your assembly>) manually instead.")]
        [SerializeField] private bool _autoInitialize = true;

        [Header("Settings")]
        [SerializeField] private PanelSide _panelSide;
        [SerializeField] private LayoutMode _layoutMode;
        [SerializeField] private PinnedPerformanceGraphsPosition _pinnedPerformanceGraphsPosition = PinnedPerformanceGraphsPosition.BottomLeft;
        [SerializeField] private float _controlWidth = 350;
        [SerializeField] private float _commandsWidth = 500;
        [SerializeField] private float _hierarchyWidth = 494;
        [SerializeField] private float _inspectorWidth = 420;
        [SerializeField, Range(0.2f, 5f)] private float _uiScale = 1f;
        [SerializeField] private Color _panelBackgroundColor = new Color(40 / 255f, 40 / 255f, 40 / 255f, 0.95f);
        [SerializeField] private Color _panelOutlineColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private DevSuitePanelActivationMode _activationMode = DevSuitePanelActivationMode.SingleClick;
        /// <summary>
        /// Controls the visibility of the expand button when the panel is collapsed.
        /// </summary>
        [FormerlySerializedAs("_expandButtonVisibility")]
        [Tooltip("Configure expand button visibility when the panel is collapsed (Visible, Hidden, or ErrorOnly).")]
        [SerializeField] private ControlPanelExpandButtonVisibility _buttonExpandVisibility = ControlPanelExpandButtonVisibility.Visible;

        public PinnedPerformanceGraphsPosition PinnedPerformanceGraphsPosition
        {
            get => _pinnedPerformanceGraphsPosition;
            set
            {
                if (_pinnedPerformanceGraphsPosition == value)
                    return;
                _pinnedPerformanceGraphsPosition = value;
                ApplyPinnedGraphsPosition();
            }
        }

        public PinnedPerformanceGraphsPosition PinnedPerformanceGraphsMode
        {
            get => _pinnedPerformanceGraphsPosition;
            set => PinnedPerformanceGraphsPosition = value;
        }

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

        [Header("Hierarchy Panel")]
        [SerializeField] private VisualTreeAsset _hierarchyUxml;
        [SerializeField] private StyleSheet _hierarchyUss;

        [Header("Inspector Panel")]
        [SerializeField] private VisualTreeAsset _inspectorUxml;
        [SerializeField] private StyleSheet _inspectorUss;

        [Header("Pick Selection")]
        [SerializeField] private StyleSheet _pickSelectionUss;

        [Header("Selection Frame")]
        [SerializeField] private StyleSheet _selectionFrameUss;

        private LogsPanelView _logsPanelView;
        private CommandsPanelView _commandsFullPanelView;
        private CommandsPanelView _commandsPinnedPanelView;
        private PerformancePanelView _performancePanelView;
        private ControlPanelView _controlView;
        private HierarchyPanelView _hierarchyPanelView;
        private InspectorPanelView _inspectorPanelView;
        private PickSelectionPanelView _pickSelectionPanelView;
        private SelectionFrameView _selectionFrameView;

        private VisualElement _logsContainer;
        private VisualElement _commandsFullContainer;
        private VisualElement _basicContainer;
        private VisualElement _pinnedContainer;
        private VisualElement _performancePanelContainer;
        private VisualElement _controlContainer;
        private VisualElement _hierarchyContainer;
        private VisualElement _inspectorContainer;
        private VisualElement _panelRootLayout;
        private VisualElement _pinnedPerformanceContainer;
        private readonly List<(BaseGraphDataProvider data, PerformanceGraphView view)> _pinnedGraphs = new();
        private bool _lastHierarchyVisible;

        private (int width, int height, Rect safeArea, float uiScale)? _lastScreen;

        private (float left, float top, float right, float bottom)? _lastSafeAreaInsets;

        private bool? _isPortrait;
        private bool IsPortrait => _isPortrait ??= _layoutMode switch
        {
            LayoutMode.Auto => Screen.height > Screen.width,
            LayoutMode.Portrait => true,
            LayoutMode.Landscape => false,
            _ => throw new ArgumentOutOfRangeException()
        };

        private DevSuiteContext _context;
        private UIDocument _uiDocument;

        internal static DevSuitePanelUI Instance { get; set; }

        private void Awake()
        {
            if (Instance != null)
            {
                throw new Exception($"{nameof(DevSuitePanelUI)} already exists. Many instances of {nameof(DevSuitePanelUI)} are not supported");
            }
            Instance = this;

            if (!DevSuiteContext.Enabled)
            {
                Debug.Log($"Initializing {nameof(DevSuitePanelUI)} is skipped because {nameof(DevSuiteContext)}.{nameof(DevSuiteContext.Enabled)}=false");
                Destroy(gameObject);
                return;
            }

            if (_autoInitialize)
            {
                DevSuiteContext.Default.Initialize(this);
            }

            _uiDocument = GetComponent<UIDocument>();
            _context = DevSuiteContext.DefaultInternal;
            _context.OnChanged += UpdateVisibility;
            _context.OnPerformanceGraphPinnedChanged += HandlePerformanceGraphPinnedChanged;
            _context.OnPerformancePanelChanged += HandlePerformancePanelChanged;

            _lastScreen = (Screen.width, Screen.height, Screen.safeArea, _uiScale);

            BuildUI();
        }

        private void Start()
        {
            if (_context != null && _context.PanelExpanded && _context.HierarchyVisible)
            {
                _hierarchyPanelView?.Refresh();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying && _uiDocument.rootVisualElement.childCount == 0)
            {
                BuildUI();
            }
            else if (_context != null && _context.PanelExpanded && _context.HierarchyVisible)
            {
                _hierarchyPanelView?.Refresh();
            }
        }

        private void LateUpdate()
        {
            var screenSizeChanged = !_lastScreen.HasValue || _lastScreen.Value.width != Screen.width || _lastScreen.Value.height != Screen.height;
            var safeAreaChanged = !_lastScreen.HasValue || _lastScreen.Value.safeArea != Screen.safeArea;
            var scaleChanged = !_lastScreen.HasValue || !Mathf.Approximately(_lastScreen.Value.uiScale, _uiScale);

            if (screenSizeChanged)
            {
                var wasPortrait = _isPortrait;
                _isPortrait = null;
                if (wasPortrait.HasValue && wasPortrait.Value != IsPortrait)
                {
                    _lastScreen = (Screen.width, Screen.height, Screen.safeArea, _uiScale);
                    BuildUI();
                    return;
                }
            }

            if (scaleChanged)
            {
                if (_uiDocument != null && _uiDocument.rootVisualElement != null)
                {
                    ApplyScale(_uiDocument.rootVisualElement);
                }
            }

            if (screenSizeChanged || safeAreaChanged || scaleChanged)
            {
                _lastScreen = (Screen.width, Screen.height, Screen.safeArea, _uiScale);
                ApplySafeArea();
            }

#if UNITY_EDITOR
            if (Application.isPlaying && _uiDocument.rootVisualElement.childCount == 0)
            {
                BuildUI();
            }
#endif
        }

        private void BuildUI()
        {
            ResetViews();

            var root = _uiDocument.rootVisualElement;
            root.Clear();

            ApplyScale(root);

            root.style.backgroundColor = StyleKeyword.Null; // Ensure it's not overridden
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ApplyColors(root);
                ApplySafeArea();
            });

            _isPortrait = null;
            var isLeft = _panelSide == PanelSide.Left;
            var layout = IsPortrait
                ? _layoutPortraitUxml
                : (isLeft ? _layoutLandscapeLeftUxml : _layoutLandscapeRightUxml);
            layout.CloneTree(root);

            root.styleSheets.Add(_layoutUss);

            _panelRootLayout = root.Q<VisualElement>(className: "devsuite-panel-root") ?? root.Q<VisualElement>("root");

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
            _controlView = new ControlPanelView(_controlUxml, _controlUss, isLeft, _activationMode, _buttonExpandVisibility);
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

            _hierarchyContainer = root.Q<VisualElement>("hierarchy-container");
            _hierarchyPanelView = new HierarchyPanelView(_hierarchyUxml, _hierarchyUss);
            _hierarchyPanelView.AddToClassList("fill-container");
            _hierarchyContainer.Add(_hierarchyPanelView);
            _hierarchyPanelView.Initialize(_context);

            _inspectorContainer = root.Q<VisualElement>("inspector-container");
            _inspectorPanelView = new InspectorPanelView(_inspectorUxml, _inspectorUss);
            _inspectorPanelView.AddToClassList("fill-container");
            _inspectorContainer.Add(_inspectorPanelView);
            _inspectorPanelView.Initialize(_context);

            _selectionFrameView = new SelectionFrameView(_selectionFrameUss);
            root.Insert(0, _selectionFrameView);
            _selectionFrameView.Initialize(_context);

            _pickSelectionPanelView = new PickSelectionPanelView(_pickSelectionUss);
            root.Add(_pickSelectionPanelView);
            _pickSelectionPanelView.Initialize(_context);

            _pinnedPerformanceContainer = new VisualElement { name = "pinned-performance-container" };
            _pinnedPerformanceContainer.AddToClassList("pinned-performance-container");
            _pinnedPerformanceContainer.pickingMode = PickingMode.Ignore;
            _pinnedPerformanceContainer.styleSheets.Add(_performanceUss);
            root.Add(_pinnedPerformanceContainer);

            ApplyColors(root);
            UpdateVisibility();
            ApplySafeArea();
            ApplyPinnedGraphsPosition();
        }

        private void OnDestroy()
        {
            Instance = null;
            Reset();
        }

        public void Reset()
        {
            if (_context != null)
            {
                _context.OnChanged -= UpdateVisibility;
                _context.OnPerformanceGraphPinnedChanged -= HandlePerformanceGraphPinnedChanged;
                _context.OnPerformancePanelChanged -= HandlePerformancePanelChanged;
                _context = null;
            }

            ResetViews();
        }

        private void ResetViews()
        {
            _lastHierarchyVisible = false;
            _logsPanelView?.Reset();
            _commandsFullPanelView?.Reset();
            _commandsPinnedPanelView?.Reset();
            _performancePanelView?.Reset();
            _controlView?.Reset();
            _hierarchyPanelView?.Reset();
            _inspectorPanelView?.Reset();
            _pickSelectionPanelView?.Reset();
            _selectionFrameView?.Reset();
            ResetPinnedGraphs();

            _logsPanelView = null;
            _commandsFullPanelView = null;
            _commandsPinnedPanelView = null;
            _performancePanelView = null;
            _controlView = null;
            _hierarchyPanelView = null;
            _inspectorPanelView = null;
            _pickSelectionPanelView = null;
            _selectionFrameView = null;

            _pinnedPerformanceContainer?.RemoveFromHierarchy();
            _pinnedPerformanceContainer = null;
            _logsContainer = null;
            _commandsFullContainer = null;
            _basicContainer = null;
            _pinnedContainer = null;
            _performancePanelContainer = null;
            _controlContainer = null;
            _hierarchyContainer = null;
            _inspectorContainer = null;
            _panelRootLayout = null;
            _lastSafeAreaInsets = null;
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

            float percent = 100f / _uiScale;
            root.style.width = Length.Percent(percent);
            root.style.height = Length.Percent(percent);
        }

        private void OnValidate()
        {
            var uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying && uiDocument.rootVisualElement.childCount == 0 && _uiDocument != null)
                {
                    BuildUI();
                    return;
                }
#endif
                ApplyScale(uiDocument.rootVisualElement);
                ApplyColors(uiDocument.rootVisualElement);
                ApplySafeArea();
                ApplyPinnedGraphsPosition();
#if UNITY_EDITOR
                if (Application.isPlaying && _panelRootLayout != null)
                {
                    UpdateVisibility();
                }
#endif
            }
        }

        private void ApplySafeArea()
        {
            if (_panelRootLayout == null && _uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                _panelRootLayout = _uiDocument.rootVisualElement.Q<VisualElement>(className: "devsuite-panel-root")
                                ?? _uiDocument.rootVisualElement.Q<VisualElement>("root");
            }

            if (_panelRootLayout == null || _uiDocument == null)
            {
                return;
            }

            var root = _uiDocument.rootVisualElement;
            if (root == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            var safeArea = Screen.safeArea;
            float left = 0f, top = 0f, right = 0f, bottom = 0f;
            var converted = false;

            if (root.panel != null)
            {
                try
                {
                    var screenTopLeftLocal = root.WorldToLocal(RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero));
                    var safeTopLeftLocal = root.WorldToLocal(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(safeArea.xMin, Screen.height - safeArea.yMax)));
                    var screenBottomRightLocal = root.WorldToLocal(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, Screen.height)));
                    var safeBottomRightLocal = root.WorldToLocal(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(safeArea.xMax, Screen.height - safeArea.yMin)));

                    left = Mathf.Max(0f, safeTopLeftLocal.x - screenTopLeftLocal.x);
                    top = Mathf.Max(0f, safeTopLeftLocal.y - screenTopLeftLocal.y);
                    right = Mathf.Max(0f, screenBottomRightLocal.x - safeBottomRightLocal.x);
                    bottom = Mathf.Max(0f, screenBottomRightLocal.y - safeBottomRightLocal.y);
                    converted = true;
                }
                catch
                {
                    converted = false;
                }
            }

            if (!converted)
            {
                var scale = _uiScale > 0 ? _uiScale : 1f;
                left = Mathf.Max(0f, safeArea.xMin / scale);
                top = Mathf.Max(0f, (Screen.height - safeArea.yMax) / scale);
                right = Mathf.Max(0f, (Screen.width - safeArea.xMax) / scale);
                bottom = Mathf.Max(0f, safeArea.yMin / scale);
            }

            if (!_lastSafeAreaInsets.HasValue ||
                !Mathf.Approximately(_lastSafeAreaInsets.Value.left, left) ||
                !Mathf.Approximately(_lastSafeAreaInsets.Value.top, top) ||
                !Mathf.Approximately(_lastSafeAreaInsets.Value.right, right) ||
                !Mathf.Approximately(_lastSafeAreaInsets.Value.bottom, bottom))
            {
                _lastSafeAreaInsets = (left, top, right, bottom);

                _panelRootLayout.style.paddingLeft = left;
                _panelRootLayout.style.paddingTop = top;
                _panelRootLayout.style.paddingRight = right;
                _panelRootLayout.style.paddingBottom = bottom;

                ApplyPinnedGraphsPosition();
            }
        }

        private void UpdateVisibility()
        {
            var expanded = _context.PanelExpanded;
            UpdatePinnedPerformanceGraphs();

            if (IsPortrait)
            {
                _basicContainer.style.width = Length.Percent(100);
                _basicContainer.style.maxWidth = StyleKeyword.Null;
                _basicContainer.style.minWidth = StyleKeyword.Null;
                _basicContainer.style.flexShrink = StyleKeyword.Null;
            }
            else
            {
                _basicContainer.style.width = expanded ? _controlWidth : StyleKeyword.Null;
                _basicContainer.style.maxWidth = expanded ? _controlWidth : StyleKeyword.Null;
                _basicContainer.style.flexShrink = 1;
            }

            var isHierarchyVisible = expanded && _context.HierarchyVisible;
            if (isHierarchyVisible && !_lastHierarchyVisible)
            {
                _hierarchyPanelView?.Refresh();
            }
            _lastHierarchyVisible = isHierarchyVisible;

            _logsContainer.style.display = expanded && _context.LogsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _commandsFullContainer.style.display = expanded && _context.CommandsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _pinnedContainer.style.display = expanded && _context.PinnedCommandsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _performancePanelContainer.style.display = expanded && _context.MetricsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _hierarchyContainer.style.display = isHierarchyVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _inspectorContainer.style.display = expanded && _context.InspectorVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if ((!expanded || !_context.HierarchyVisible) && _context.PickModeActive)
            {
                _context.PickModeActive = false;
            }

            if (IsPortrait)
            {
                _commandsFullContainer.style.width = StyleKeyword.Auto;
                _commandsFullContainer.style.maxWidth = StyleKeyword.Null;
                _commandsFullContainer.style.flexGrow = 1;
                _commandsFullContainer.style.flexShrink = 1;
                _commandsFullContainer.style.flexBasis = 0;

                _logsContainer.style.width = StyleKeyword.Null;
                _logsContainer.style.maxWidth = StyleKeyword.Null;
                _logsContainer.style.flexGrow = 1;
                _logsContainer.style.flexShrink = 1;
                _logsContainer.style.flexBasis = 0;

                _hierarchyContainer.style.width = StyleKeyword.Auto;
                _hierarchyContainer.style.maxWidth = StyleKeyword.Null;
                _hierarchyContainer.style.flexGrow = 1;
                _hierarchyContainer.style.flexShrink = 1;
                _hierarchyContainer.style.flexBasis = 0;

                _inspectorContainer.style.width = StyleKeyword.Auto;
                _inspectorContainer.style.maxWidth = StyleKeyword.Null;
                _inspectorContainer.style.flexGrow = 1;
                _inspectorContainer.style.flexShrink = 1;
                _inspectorContainer.style.flexBasis = 0;

                _commandsFullContainer.style.alignSelf = StyleKeyword.Null;
                _commandsFullContainer.style.maxHeight = StyleKeyword.Null;
                _logsContainer.style.alignSelf = StyleKeyword.Null;
                _logsContainer.style.maxHeight = StyleKeyword.Null;
                _hierarchyContainer.style.alignSelf = StyleKeyword.Null;
                _hierarchyContainer.style.maxHeight = StyleKeyword.Null;
                _inspectorContainer.style.alignSelf = StyleKeyword.Null;
                _inspectorContainer.style.maxHeight = StyleKeyword.Null;
            }
            else
            {
                _commandsFullContainer.style.width = StyleKeyword.Null;
                _commandsFullContainer.style.maxWidth = expanded ? _commandsWidth : StyleKeyword.Null;
                _commandsFullContainer.style.flexGrow = 0;
                _commandsFullContainer.style.flexShrink = 1;
                _commandsFullContainer.style.flexBasis = expanded ? _commandsWidth : StyleKeyword.Null;

                _logsContainer.style.width = StyleKeyword.Null;
                _logsContainer.style.maxWidth = StyleKeyword.Null;
                _logsContainer.style.flexGrow = StyleKeyword.Null;
                _logsContainer.style.flexShrink = StyleKeyword.Null;
                _logsContainer.style.flexBasis = StyleKeyword.Null;

                _hierarchyContainer.style.width = StyleKeyword.Null;
                _hierarchyContainer.style.maxWidth = expanded ? _hierarchyWidth : StyleKeyword.Null;
                _hierarchyContainer.style.flexGrow = 0;
                _hierarchyContainer.style.flexShrink = 1;
                _hierarchyContainer.style.flexBasis = expanded ? _hierarchyWidth : StyleKeyword.Null;

                _inspectorContainer.style.width = StyleKeyword.Null;
                _inspectorContainer.style.maxWidth = expanded ? _inspectorWidth : StyleKeyword.Null;
                _inspectorContainer.style.flexGrow = 0;
                _inspectorContainer.style.flexShrink = 1;
                _inspectorContainer.style.flexBasis = expanded ? _inspectorWidth : StyleKeyword.Null;

                _commandsFullContainer.style.alignSelf = Align.FlexStart;
                _commandsFullContainer.style.maxHeight = Length.Percent(100);
                _logsContainer.style.alignSelf = Align.FlexStart;
                _logsContainer.style.maxHeight = Length.Percent(100);
                _hierarchyContainer.style.alignSelf = Align.FlexStart;
                _hierarchyContainer.style.maxHeight = Length.Percent(100);
                _inspectorContainer.style.alignSelf = Align.FlexStart;
                _inspectorContainer.style.maxHeight = Length.Percent(100);
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

        private void HandlePerformanceGraphPinnedChanged(BaseGraphDataProvider provider, bool isPinned)
        {
            UpdatePinnedPerformanceGraphs();
        }

        private void HandlePerformancePanelChanged()
        {
            UpdatePinnedPerformanceGraphs();
        }

        private void UpdatePinnedPerformanceGraphs()
        {
            if (_pinnedPerformanceContainer == null)
            {
                return;
            }

            if (_context == null || _context.PanelExpanded)
            {
                _pinnedPerformanceContainer.style.display = DisplayStyle.None;
                return;
            }

            var pinnedProviders = new List<BaseGraphDataProvider>();
            foreach (var provider in _context.PerformancePanelProviders)
            {
                if (_context.IsPerformanceGraphPinned(provider))
                {
                    pinnedProviders.Add(provider);
                }
            }

            if (pinnedProviders.Count == 0)
            {
                _pinnedPerformanceContainer.style.display = DisplayStyle.None;
                ResetPinnedGraphs();
                return;
            }

            _pinnedPerformanceContainer.style.display = DisplayStyle.Flex;

            var matches = _pinnedGraphs.Count == pinnedProviders.Count;
            if (matches)
            {
                for (var i = 0; i < pinnedProviders.Count; i++)
                {
                    if (_pinnedGraphs[i].data != pinnedProviders[i])
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (matches)
            {
                foreach (var (_, view) in _pinnedGraphs)
                {
                    view.AlwaysExtended = true;
                    view.IsPinnedMode = true;
                    view.SetClickThrough(true);
                    view.UpdateViewState();
                }
                UpdatePinnedGraphsLastClass();
                SetPickingModeRecursive(_pinnedPerformanceContainer, PickingMode.Ignore);
                return;
            }

            ResetPinnedGraphs();

            for (var i = 0; i < pinnedProviders.Count; i++)
            {
                var provider = pinnedProviders[i];
                var graphView = new PerformanceGraphView(provider, _context);
                graphView.AlwaysExtended = true;
                graphView.IsPinnedMode = true;
                graphView.SetClickThrough(true);
                _pinnedPerformanceContainer.Add(graphView);
                _pinnedGraphs.Add((provider, graphView));
            }

            UpdatePinnedGraphsLastClass();
            SetPickingModeRecursive(_pinnedPerformanceContainer, PickingMode.Ignore);
            ApplyPinnedGraphsPosition();
        }

        private void UpdatePinnedGraphsLastClass()
        {
            if (_pinnedPerformanceContainer == null)
            {
                return;
            }

            for (var i = 0; i < _pinnedPerformanceContainer.childCount; i++)
            {
                var child = _pinnedPerformanceContainer[i];
                if (i == _pinnedPerformanceContainer.childCount - 1)
                {
                    child.AddToClassList("graph-view--last");
                }
                else
                {
                    child.RemoveFromClassList("graph-view--last");
                }
            }
        }

        private void ResetPinnedGraphs()
        {
            foreach (var (_, view) in _pinnedGraphs)
            {
                view.Reset();
            }
            _pinnedGraphs.Clear();
            _pinnedPerformanceContainer?.Clear();
        }

        private void ApplyPinnedGraphsPosition()
        {
            if (_pinnedPerformanceContainer == null)
            {
                return;
            }

            const float margin = 4f;
            const float expandButtonOffset = 32f;

            var insetsLeft = _lastSafeAreaInsets?.left ?? 0f;
            var insetsTop = _lastSafeAreaInsets?.top ?? 0f;
            var insetsRight = _lastSafeAreaInsets?.right ?? 0f;
            var insetsBottom = _lastSafeAreaInsets?.bottom ?? 0f;

            _pinnedPerformanceContainer.style.position = Position.Absolute;
            _pinnedPerformanceContainer.style.top = StyleKeyword.Null;
            _pinnedPerformanceContainer.style.bottom = StyleKeyword.Null;
            _pinnedPerformanceContainer.style.left = StyleKeyword.Null;
            _pinnedPerformanceContainer.style.right = StyleKeyword.Null;

            switch (_pinnedPerformanceGraphsPosition)
            {
                case PinnedPerformanceGraphsPosition.TopLeft:
                {
                    var top = insetsTop + margin;
                    if (_panelSide == PanelSide.Left)
                    {
                        top += expandButtonOffset;
                    }
                    _pinnedPerformanceContainer.style.top = top;
                    _pinnedPerformanceContainer.style.left = insetsLeft + margin;
                    _pinnedPerformanceContainer.style.width = 260f;
                    _pinnedPerformanceContainer.style.alignItems = Align.FlexStart;
                    break;
                }
                case PinnedPerformanceGraphsPosition.Top:
                {
                    _pinnedPerformanceContainer.style.top = insetsTop + margin;
                    _pinnedPerformanceContainer.style.left = insetsLeft + margin;
                    _pinnedPerformanceContainer.style.right = insetsRight + margin;
                    _pinnedPerformanceContainer.style.width = StyleKeyword.Null;
                    _pinnedPerformanceContainer.style.alignItems = Align.Center;
                    break;
                }
                case PinnedPerformanceGraphsPosition.TopRight:
                {
                    var top = insetsTop + margin;
                    if (_panelSide == PanelSide.Right)
                    {
                        top += expandButtonOffset;
                    }
                    _pinnedPerformanceContainer.style.top = top;
                    _pinnedPerformanceContainer.style.right = insetsRight + margin;
                    _pinnedPerformanceContainer.style.width = 260f;
                    _pinnedPerformanceContainer.style.alignItems = Align.FlexEnd;
                    break;
                }
                case PinnedPerformanceGraphsPosition.BottomLeft:
                {
                    _pinnedPerformanceContainer.style.bottom = insetsBottom + margin;
                    _pinnedPerformanceContainer.style.left = insetsLeft + margin;
                    _pinnedPerformanceContainer.style.width = 260f;
                    _pinnedPerformanceContainer.style.alignItems = Align.FlexStart;
                    break;
                }
                case PinnedPerformanceGraphsPosition.Bottom:
                {
                    _pinnedPerformanceContainer.style.bottom = insetsBottom + margin;
                    _pinnedPerformanceContainer.style.left = insetsLeft + margin;
                    _pinnedPerformanceContainer.style.right = insetsRight + margin;
                    _pinnedPerformanceContainer.style.width = StyleKeyword.Null;
                    _pinnedPerformanceContainer.style.alignItems = Align.Center;
                    break;
                }
                case PinnedPerformanceGraphsPosition.BottomRight:
                {
                    _pinnedPerformanceContainer.style.bottom = insetsBottom + margin;
                    _pinnedPerformanceContainer.style.right = insetsRight + margin;
                    _pinnedPerformanceContainer.style.width = 260f;
                    _pinnedPerformanceContainer.style.alignItems = Align.FlexEnd;
                    break;
                }
            }
        }

        private static void SetPickingModeRecursive(VisualElement element, PickingMode mode)
        {
            if (element == null)
            {
                return;
            }

            element.pickingMode = mode;
            for (var i = 0; i < element.childCount; i++)
            {
                SetPickingModeRecursive(element[i], mode);
            }
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

    public enum PinnedPerformanceGraphsPosition
    {
        TopLeft,
        Top,
        TopRight,
        BottomLeft,
        Bottom,
        BottomRight,
    }

    public enum PinnedPerformanceGraphsMode
    {
        TopLeft = PinnedPerformanceGraphsPosition.TopLeft,
        Top = PinnedPerformanceGraphsPosition.Top,
        TopRight = PinnedPerformanceGraphsPosition.TopRight,
        BottomLeft = PinnedPerformanceGraphsPosition.BottomLeft,
        Bottom = PinnedPerformanceGraphsPosition.Bottom,
        BottomRight = PinnedPerformanceGraphsPosition.BottomRight,
    }
}