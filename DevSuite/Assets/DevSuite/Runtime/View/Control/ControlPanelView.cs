using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class ControlPanelView : VisualElement
    {
        private DevSuiteContext _context;

        private readonly Button _resetButton;
        private readonly VisualElement _resetProgressBar;
        private readonly Button _logsButton;
        private readonly Button _commandsButton;
        private readonly Button _pinnedCommandsButton;
        private readonly Button _metricsButton;
        private readonly Button _hierarchyButton;
        private readonly Button _inspectorButton;
        private readonly Button _expandButton;
        private readonly VisualElement _divider;
        private readonly Label _versionLabel;
        private readonly Button _unpauseButton;

        private bool _isPaused;
        private IVisualElementScheduledItem _pauseCheckTask;
        private static readonly Color ColorOrangeBase = new(1f, 200f / 255f, 0f, 1f);
        private static readonly Color ColorOrangeGlow = new(1f, 225f / 255f, 80f / 255f, 1f);

        private bool _hasUnseenErrors;
        private IVisualElementScheduledItem _blinkTask;
        private readonly DevSuitePanelActivationMode _activationMode;
        private readonly ControlPanelExpandButtonVisibility _expandButtonVisibility;

        private const float ResetHoldDuration = 2.0f;
        private const float ResetClickThreshold = 0.2f;

        private int _clickCount;
        private IVisualElementScheduledItem _clickResetTask;
        private IVisualElementScheduledItem _holdTask;
        private IVisualElementScheduledItem _resetHoldTask;
        private bool _resetHoldCompleted;
        private bool _resetHoldCancelled;
        private float _resetHoldStartTime;
        private IVisualElementScheduledItem _warningTask;
        private float _lastToggleTime;

        private const string IconReset = "\uf2ed";
        private const string IconLogs = "\uf02d";
        private const string IconCommands = "\uf04e";
        private const string IconPinnedCommands = "\uf08d";
        private const string IconMetrics = "\uf681";
        private const string IconHierarchy = "\uf0e8";
        private const string IconInspector = "\uf05a";
        private const string IconExpand = "\uf0fe";
        private const string IconCollapse = "\uf146";
        private const int BlinkIntervalMs = 300;

        internal ControlPanelView(VisualTreeAsset uxml, StyleSheet uss, bool reverse, DevSuitePanelActivationMode activationMode, ControlPanelExpandButtonVisibility expandButtonVisibility)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            AddToClassList("ff-control-panel");
            AddToClassList("ff-panel");

            _resetButton = this.Q<Button>("reset-btn");
            _resetProgressBar = new VisualElement { name = "reset-btn-progress", pickingMode = PickingMode.Ignore };
            _resetProgressBar.AddToClassList("ff-control-panel-btn-progress");
            _resetProgressBar.style.display = DisplayStyle.None;
            _resetButton.Insert(0, _resetProgressBar);
            _logsButton = this.Q<Button>("logs-btn");
            _commandsButton = this.Q<Button>("commands-btn");
            _pinnedCommandsButton = this.Q<Button>("pinned-commands-btn");
            _metricsButton = this.Q<Button>("metrics-btn");
            _hierarchyButton = this.Q<Button>("hierarchy-btn");
            _inspectorButton = this.Q<Button>("inspector-btn");
            _expandButton = this.Q<Button>("expand-btn");
            _divider = this.Q<VisualElement>("divider");
            _versionLabel = this.Q<Label>("version-lbl");
            _unpauseButton = this.Q<Button>("unpause-btn");
            if (_unpauseButton != null)
            {
                _unpauseButton.clicked += HandleUnpauseClicked;
            }

            _resetButton.text = IconReset;
            _logsButton.text = IconLogs;
            _commandsButton.text = IconCommands;
            _pinnedCommandsButton.text = IconPinnedCommands;
            _metricsButton.text = IconMetrics;
            _hierarchyButton.text = IconHierarchy;
            _inspectorButton.text = IconInspector;
            _expandButton.text = IconExpand;

            var root = this.Q<VisualElement>("ff-control-panel-root");
            if (reverse)
            {
                root.style.flexDirection = FlexDirection.RowReverse;
            }

            var lastChild = root.ElementAt(reverse ? 0 : root.childCount - 1);
            lastChild?.AddToClassList("last-item");

            var firstChild = root.ElementAt(reverse ? root.childCount - 1 : 0);
            firstChild?.AddToClassList("first-item");

            _activationMode = activationMode;
            _expandButtonVisibility = expandButtonVisibility;

            _resetButton.RegisterCallback<PointerDownEvent>(HandleResetPointerDown, TrickleDown.TrickleDown);
            _resetButton.RegisterCallback<PointerUpEvent>(HandleResetPointerUp, TrickleDown.TrickleDown);
            _resetButton.RegisterCallback<PointerLeaveEvent>(HandleResetPointerLeave, TrickleDown.TrickleDown);
            _resetButton.RegisterCallback<PointerCancelEvent>(HandleResetPointerLeave, TrickleDown.TrickleDown);
            _resetButton.RegisterCallback<MouseDownEvent>(HandleResetMouseDown, TrickleDown.TrickleDown);
            _resetButton.RegisterCallback<MouseUpEvent>(HandleResetMouseUp, TrickleDown.TrickleDown);
            _resetButton.RegisterCallback<MouseLeaveEvent>(HandleResetPointerLeave, TrickleDown.TrickleDown);
            _resetButton.clicked += HandleResetClicked;
            _logsButton.clicked += () => ToggleContextValue(ctx => ctx.LogsVisible = !ctx.LogsVisible);
            _commandsButton.clicked += () => ToggleContextValue(ctx => ctx.CommandsVisible = !ctx.CommandsVisible);
            _pinnedCommandsButton.clicked += () => ToggleContextValue(ctx => ctx.PinnedCommandsVisible = !ctx.PinnedCommandsVisible);
            _metricsButton.clicked += () => ToggleContextValue(ctx => ctx.MetricsVisible = !ctx.MetricsVisible);
            _hierarchyButton.clicked += () => ToggleContextValue(ctx => ctx.HierarchyVisible = !ctx.HierarchyVisible);
            _inspectorButton.clicked += () => ToggleContextValue(ctx => ctx.InspectorVisible = !ctx.InspectorVisible);

            _expandButton.RegisterCallback<PointerDownEvent>(HandleExpandPointerDown, TrickleDown.TrickleDown);
            _expandButton.RegisterCallback<PointerUpEvent>(HandleExpandPointerUp, TrickleDown.TrickleDown);
            _expandButton.RegisterCallback<PointerLeaveEvent>(HandleExpandPointerUp, TrickleDown.TrickleDown);
            _expandButton.RegisterCallback<MouseDownEvent>(HandleExpandMouseDown, TrickleDown.TrickleDown);
            _expandButton.RegisterCallback<MouseUpEvent>(HandleExpandMouseUp, TrickleDown.TrickleDown);
            _expandButton.RegisterCallback<MouseLeaveEvent>(HandleExpandMouseUp, TrickleDown.TrickleDown);
            _expandButton.clicked += HandleExpandClicked;
            DevSuiteUtils.SetupTooltips(this);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.pauseStateChanged -= HandleEditorPauseStateChanged;
                UnityEditor.EditorApplication.pauseStateChanged += HandleEditorPauseStateChanged;
                UnityEditor.EditorApplication.update -= HandleEditorUpdate;
                UnityEditor.EditorApplication.update += HandleEditorUpdate;
#endif
                StartPauseMonitoring();
            });

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.pauseStateChanged -= HandleEditorPauseStateChanged;
                UnityEditor.EditorApplication.update -= HandleEditorUpdate;
#endif
                StopPauseMonitoring();
            });

#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= HandleEditorPauseStateChanged;
            UnityEditor.EditorApplication.pauseStateChanged += HandleEditorPauseStateChanged;
            UnityEditor.EditorApplication.update -= HandleEditorUpdate;
            UnityEditor.EditorApplication.update += HandleEditorUpdate;
#endif
            StartPauseMonitoring();
        }

        private void HandleExpandClicked()
        {
            if (Time.unscaledTime - _lastToggleTime < 1.0f)
            {
                ShowCooldownFeedback();
                return;
            }

            if (_context.PanelExpanded)
            {
                // Always collapse on single click
                _lastToggleTime = Time.unscaledTime;
                ToggleContextValue(ctx => ctx.PanelExpanded = false);
                return;
            }

            if (_activationMode == DevSuitePanelActivationMode.SingleClick)
            {
                _lastToggleTime = Time.unscaledTime;
                ToggleContextValue(ctx => ctx.PanelExpanded = true);
            }
            else if (_activationMode == DevSuitePanelActivationMode.MultiClick)
            {
                _clickCount++;
                _clickResetTask?.Pause();
                _clickResetTask = schedule.Execute(() => _clickCount = 0).StartingIn(1000);

                if (_clickCount >= 5)
                {
                    _clickCount = 0;
                    _clickResetTask?.Pause();
                    _lastToggleTime = Time.unscaledTime;
                    ToggleContextValue(ctx => ctx.PanelExpanded = true);
                }
            }
        }

        private void HandleExpandMouseDown(MouseDownEvent evt) => HandleHoldStart();
        private void HandleExpandMouseUp(EventBase evt) => HandleHoldStop();
        private void HandleExpandPointerDown(PointerDownEvent evt) => HandleHoldStart();
        private void HandleExpandPointerUp(EventBase evt) => HandleHoldStop();

        private void HandleHoldStart()
        {
            if (_context.PanelExpanded || _activationMode != DevSuitePanelActivationMode.Hold)
                return;

            if (Time.unscaledTime - _lastToggleTime < 0.5f)
            {
                ShowCooldownFeedback();
                return;
            }

            _holdTask?.Pause();
            _holdTask = schedule.Execute(() =>
            {
                _lastToggleTime = Time.unscaledTime;
                ToggleContextValue(ctx => ctx.PanelExpanded = true);
                _holdTask = null;
            }).StartingIn(2000);
        }

        private void HandleHoldStop()
        {
            _holdTask?.Pause();
            _holdTask = null;
        }

        private void HandleResetMouseDown(MouseDownEvent evt) => HandleResetHoldStart();
        private void HandleResetMouseUp(EventBase evt) => HandleResetHoldStop();
        private void HandleResetPointerDown(PointerDownEvent evt) => HandleResetHoldStart();
        private void HandleResetPointerUp(EventBase evt) => HandleResetHoldStop();

        private void HandleResetHoldStart()
        {
            if (_resetHoldTask != null)
                return;

            _resetHoldCompleted = false;
            _resetHoldCancelled = false;
            _resetHoldStartTime = GetCurrentTime();
            SetResetProgress(0f);

            _resetHoldTask = schedule.Execute(() =>
            {
                var elapsed = GetCurrentTime() - _resetHoldStartTime;
                var progress = Mathf.Clamp01(elapsed / ResetHoldDuration);
                SetResetProgress(progress);

                if (elapsed >= ResetHoldDuration)
                {
                    _resetHoldCompleted = true;
                    _resetHoldTask.Pause();
                    _resetHoldTask = null;
                    SetResetProgress(0f);

                    if (_context != null)
                    {
                        _context.ClearAllSavedPrefs();
                    }
                    else
                    {
                        Prefs.SavedPrefs.ClearAll();
                    }
                    DevSuiteUtils.ShowIconButtonClickedFeedback(_resetButton);
                    UpdateView();
                }
            }).Every(16);
        }

        private void HandleResetHoldStop()
        {
            if (_resetHoldTask != null)
            {
                var elapsed = GetCurrentTime() - _resetHoldStartTime;
                if (elapsed >= ResetClickThreshold)
                {
                    _resetHoldCancelled = true;
                }

                _resetHoldTask.Pause();
                _resetHoldTask = null;
                SetResetProgress(0f);
            }
        }

        private void HandleResetPointerLeave(EventBase evt)
        {
            if (_resetHoldTask != null)
            {
                _resetHoldCancelled = true;
                _resetHoldTask.Pause();
                _resetHoldTask = null;
                SetResetProgress(0f);
            }
        }

        private void SetResetProgress(float progress)
        {
            if (_resetProgressBar == null)
                return;

            if (progress <= 0f)
            {
                _resetProgressBar.style.display = DisplayStyle.None;
                _resetProgressBar.style.width = Length.Percent(0);
                return;
            }

            _resetProgressBar.style.display = DisplayStyle.Flex;
            _resetProgressBar.style.width = Length.Percent(progress * 100f);

            var colorBar = Color.Lerp(ColorOrangeBase, new Color(1f, 102f / 255f, 102f / 255f, 1f), progress);
            var colorBg = new Color(colorBar.r, colorBar.g, colorBar.b, Mathf.Lerp(0.18f, 0.40f, progress));
            _resetProgressBar.style.backgroundColor = colorBg;
            _resetProgressBar.style.borderBottomColor = colorBar;
        }

        private void ShowCooldownFeedback()
        {
            _warningTask?.Pause();
            _expandButton.AddToClassList("warning");
            _warningTask = schedule.Execute(() => _expandButton.RemoveFromClassList("warning")).StartingIn(100);
        }

        public void Initialize(DevSuiteContext context)
        {
            if (_context != null)
            {
                _context.OnChanged -= UpdateView;
                _context.OnLogMessagesMessageAdded -= HandleLogMessageAdded;
                _context.PauseHandlerGameSpeed.OnChanged -= HandlePauseHandlerGameSpeedChanged;
            }

            _context = context;

            if (_context != null)
            {
                _context.OnChanged += UpdateView;
                _context.OnLogMessagesMessageAdded += HandleLogMessageAdded;
                _context.PauseHandlerGameSpeed.OnChanged += HandlePauseHandlerGameSpeedChanged;

                _hasUnseenErrors = false;
                foreach (var logMessage in _context.AllLogMessages)
                {
                    HandleLogMessageAdded(logMessage);
                }

                StartPauseMonitoring();
                UpdateView();
            }
        }


        private void ToggleContextValue(Action<DevSuiteContext> toggleAction)
        {
            toggleAction(_context);
            UpdateView();
        }

        private void HandleResetClicked()
        {
            if (_resetHoldCompleted)
            {
                _resetHoldCompleted = false;
                return;
            }

            if (_resetHoldCancelled)
            {
                _resetHoldCancelled = false;
                return;
            }

            _context?.ClearSettings();
            DevSuiteUtils.ShowIconButtonClickedFeedback(_resetButton);
            UpdateView();
        }

        private void HandleLogMessageAdded(LogMessageData message)
        {
            if (message.Level != GeneralizedLogSeverity.Error)
                return;

            if (IsErrorsPanelVisible())
                return;

            _hasUnseenErrors = true;
            UpdateErrorBlink();
            UpdateExpandButtonVisibility();
        }

        private bool IsErrorsPanelVisible()
        {
            return _context is { PanelExpanded: true, LogsVisible: true }
                   && !_context.HiddenLogSeverity.Contains(GeneralizedLogSeverity.Error);
        }

        private void UpdateErrorBlink()
        {
            if (IsErrorsPanelVisible())
            {
                _hasUnseenErrors = false;
            }

            if (_hasUnseenErrors)
            {
                var target = GetBlinkTarget();
                if (target == _logsButton)
                {
                    _expandButton.RemoveFromClassList("error-blink");
                }
                else
                {
                    _logsButton.RemoveFromClassList("error-blink");
                }

                StartBlinking();
            }
            else
            {
                StopBlinking();
            }
        }

        private void StartBlinking()
        {
            if (_blinkTask != null)
            {
                return;
            }

            _blinkTask = schedule.Execute(() =>
            {
                var target = GetBlinkTarget();
                if (target == null)
                {
                    return;
                }

                target.EnableInClassList("error-blink", !target.ClassListContains("error-blink"));
            }).Every(BlinkIntervalMs);
        }

        private void StopBlinking()
        {
            _blinkTask?.Pause();
            _blinkTask = null;

            _expandButton.RemoveFromClassList("error-blink");
            _logsButton.RemoveFromClassList("error-blink");
        }

        private Button GetBlinkTarget()
        {
            if (_context == null)
            {
                return null;
            }

            return _context.PanelExpanded ? _logsButton : _expandButton;
        }

        private void UpdateView()
        {
            var expanded = _context != null && _context.PanelExpanded;
            _isPaused = CheckIsPaused();

            EnableInClassList("collapsed", !expanded);

            _resetButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _divider.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;

            if (_isPaused)
            {
                _versionLabel.style.display = DisplayStyle.None;
                if (_unpauseButton != null)
                {
                    _unpauseButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
            else
            {
                if (_unpauseButton != null)
                {
                    _unpauseButton.style.display = DisplayStyle.None;
                }

                var extraVersion = _context?.BuildVersionToDisplay?.Invoke();
                if (string.IsNullOrEmpty(extraVersion))
                {
                    _versionLabel.style.display = DisplayStyle.None;
                }
                else
                {
                    _versionLabel.text = extraVersion;
                    _versionLabel.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            _logsButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _commandsButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _pinnedCommandsButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _metricsButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _hierarchyButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _inspectorButton.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;

            _expandButton.text = expanded ? IconCollapse : IconExpand;
            _expandButton.EnableInClassList("active", expanded);

            if (_context != null)
            {
                _logsButton.EnableInClassList("active", _context.LogsVisible);
                _commandsButton.EnableInClassList("active", _context.CommandsVisible);
                _pinnedCommandsButton.EnableInClassList("active", _context.PinnedCommandsVisible);
                _metricsButton.EnableInClassList("active", _context.MetricsVisible);
                _hierarchyButton.EnableInClassList("active", _context.HierarchyVisible);
                _inspectorButton.EnableInClassList("active", _context.InspectorVisible);
            }
            _resetButton.EnableInClassList("normal", true);

            UpdateErrorBlink();
            UpdateExpandButtonVisibility();
            if (_isPaused)
            {
                UpdatePingPongAnimation();
            }
        }

        private void HandlePauseHandlerGameSpeedChanged(float speed)
        {
            UpdatePauseState();
        }

        private void HandleUnpauseClicked()
        {
            if (_context != null)
            {
                _context.ResetPause();
            }
            else
            {
                Time.timeScale = 1.0f;
            }
            UpdatePauseState();
        }

        private bool CheckIsPaused()
        {
            if (Mathf.Approximately(Time.timeScale, 0f) || Time.timeScale < 0.0001f)
            {
                return true;
            }

            if (_context != null && _context.PauseHandlerGameSpeed.Value == 0f)
            {
                return true;
            }

#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused)
            {
                return true;
            }
#endif
            return false;
        }

        private void UpdatePauseState()
        {
            var isPaused = CheckIsPaused();
            if (_isPaused != isPaused)
            {
                _isPaused = isPaused;
                UpdateView();
            }
        }

        private void UpdatePingPongAnimation()
        {
            if (!_isPaused || _unpauseButton == null || _unpauseButton.style.display == DisplayStyle.None)
            {
                return;
            }

            var time = GetCurrentTime();
            var t = Mathf.PingPong(time * 1.5f, 1f);
            t = Mathf.SmoothStep(0f, 1f, t);
            var color = Color.Lerp(ColorOrangeBase, ColorOrangeGlow, t);
            _unpauseButton.style.color = color;
        }

        private static float GetCurrentTime()
        {
#if UNITY_EDITOR
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.unscaledTime;
#endif
        }

        private void StartPauseMonitoring()
        {
            if (_pauseCheckTask != null)
            {
                return;
            }

            _pauseCheckTask = schedule.Execute(() =>
            {
                UpdatePauseState();
                if (_isPaused)
                {
                    UpdatePingPongAnimation();
                }
            }).Every(30);
        }

        private void StopPauseMonitoring()
        {
            _pauseCheckTask?.Pause();
            _pauseCheckTask = null;
        }

#if UNITY_EDITOR
        private void HandleEditorUpdate()
        {
            if (_isPaused)
            {
                UpdatePauseState();
                UpdatePingPongAnimation();
                _unpauseButton?.MarkDirtyRepaint();
            }
            else
            {
                if (CheckIsPaused())
                {
                    UpdatePauseState();
                }
            }
        }

        private void HandleEditorPauseStateChanged(UnityEditor.PauseState state)
        {
            UpdatePauseState();
        }
#endif

        private void UpdateExpandButtonVisibility()
        {
            var expanded = _context != null && _context.PanelExpanded;
            var shouldShow = expanded; // Always show if expanded

            if (!expanded)
            {
                switch (_expandButtonVisibility)
                {
                    case ControlPanelExpandButtonVisibility.Visible:
                        shouldShow = true;
                        break;
                    case ControlPanelExpandButtonVisibility.Hidden:
                        shouldShow = false;
                        break;
                    case ControlPanelExpandButtonVisibility.ErrorOnly:
                        shouldShow = _hasUnseenErrors;
                        break;
                }
            }

            _expandButton.style.opacity = shouldShow ? 1f : 0f;
            _expandButton.style.display = DisplayStyle.Flex;
        }

        public void Reset()
        {
            StopPauseMonitoring();
            StopBlinking();
            _holdTask?.Pause();
            _holdTask = null;
            _resetHoldTask?.Pause();
            _resetHoldTask = null;
            _resetHoldCompleted = false;
            _resetHoldCancelled = false;
            SetResetProgress(0f);
            _hasUnseenErrors = false;
            _isPaused = false;

            if (_context != null)
            {
                _context.OnChanged -= UpdateView;
                _context.OnLogMessagesMessageAdded -= HandleLogMessageAdded;
                _context.PauseHandlerGameSpeed.OnChanged -= HandlePauseHandlerGameSpeedChanged;
                _context = null;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= HandleEditorPauseStateChanged;
            UnityEditor.EditorApplication.update -= HandleEditorUpdate;
#endif
        }
    }

    internal enum DevSuitePanelActivationMode
    {
        SingleClick,
        MultiClick,
        Hold,
    }

    internal enum ControlPanelExpandButtonVisibility
    {
        Visible,
        Hidden,
        ErrorOnly,
    }
}