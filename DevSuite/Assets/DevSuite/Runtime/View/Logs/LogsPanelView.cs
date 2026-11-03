using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ff.DevSuite.Commands;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class LogsPanelView : VisualElement
    {
        private DevSuiteContext _context;
        private const float RowHeight = 22f;
        private readonly VisualElement _topSpacer;
        private readonly VisualElement _itemsContainer;
        private readonly VisualElement _bottomSpacer;
        private readonly List<LogItemView> _itemPool = new();
        private readonly List<LogMessageData> _filteredMessages = new();
        private readonly Dictionary<LogMessageData, float> _expandedHeights = new();

        private readonly TextField _filterField;
        private readonly Button _clearFilterBtn;
        private readonly Button _regexButton;

        private readonly Button _ordinaryButton;
        private readonly Button _warningButton;
        private readonly Button _errorButton;
        private readonly Label _ordinaryCountLabel;
        private readonly Label _warningCountLabel;
        private readonly Label _errorCountLabel;

        private readonly Button _copyButton;
        private readonly Button _saveButton;
        private readonly Button _folderButton;
        private readonly Button _clearButton;

        private readonly ScrollView _scrollView;

        private readonly TextField _cliInputField;
        private readonly TextField _cliGhostField;
        private readonly Button _cliSendButton;
        private readonly VisualElement _cliTooltipContainer;
        private readonly ScrollView _cliTooltipScrollView;
        private bool _isPointerOverTooltip;
        private bool _cliInputFocused;
        private int _cliHistoryIndex = -1;
        private string _cliDraftText = string.Empty;

        private int _ordinaryCount, _warningCount, _errorCount;

        private readonly HashSet<GeneralizedLogSeverity> _collectStackTraceFor = new()
        {
            GeneralizedLogSeverity.Warning,
            GeneralizedLogSeverity.Error,
        };

        private const string SaveFolderPath = "logger_panel";

        public LogsPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            style.flexGrow = 1;
            style.flexShrink = 1;
            style.minHeight = 0;
            AddToClassList("ff-panel");

            var root = this.Q<VisualElement>("logs-panel-root") ?? this;

            _filterField = root.Q<TextField>("filterField");
            DevSuiteUiUtils.SetupInputFieldFocus(_filterField);
            _filterField.RegisterValueChangedCallback(evt => HandleTextChanged(evt.newValue));
            _filterField.RegisterCallback<FocusOutEvent>(
                evt =>
                {
                    if (_context != null)
                    {
                        _filterField.SetValueWithoutNotify(_context.LogsPattern);
                        UpdateClearFilterButton();
                    }
                }
            );

            _clearFilterBtn = root.Q<Button>("clearFilterBtn");
            if (_clearFilterBtn != null)
            {
                _clearFilterBtn.text = "\ue1b2";
                _clearFilterBtn.clicked += () =>
                {
                    _context.LogsPattern = "";
                    _filterField.value = "";
                    UpdateClearFilterButton();
                };
            }
            UpdateClearFilterButton();

            _regexButton = root.Q<Button>("regexButton");
            _regexButton.clicked += HandleRegexPressed;

            _ordinaryButton = root.Q<Button>("ordinaryButton");
            _ordinaryButton.clicked += () => HandleSeverityClick(GeneralizedLogSeverity.Ordinary);
            _ordinaryCountLabel = _ordinaryButton.Q<Label>("ordinaryCount");

            _warningButton = root.Q<Button>("warningButton");
            _warningButton.clicked += () => HandleSeverityClick(GeneralizedLogSeverity.Warning);
            _warningCountLabel = _warningButton.Q<Label>("warningCount");

            _errorButton = root.Q<Button>("errorButton");
            _errorButton.clicked += () => HandleSeverityClick(GeneralizedLogSeverity.Error);
            _errorCountLabel = _errorButton.Q<Label>("errorCount");

            _copyButton = root.Q<Button>("copyButton");
            _copyButton.clicked += () =>
            {
                HandleCopyPressed();
                DevSuiteUiUtils.ShowIconButtonClickedFeedback(_copyButton);
            };

            _saveButton = root.Q<Button>("saveButton");
            _saveButton.clicked += () =>
            {
                HandleSavePressed();
                DevSuiteUiUtils.ShowIconButtonClickedFeedback(_saveButton);
            };

            _folderButton = root.Q<Button>("folderButton");
            _folderButton.clicked += () =>
            {
                HandleFolderPressed();
                DevSuiteUiUtils.ShowIconButtonClickedFeedback(_folderButton);
            };

            _clearButton = root.Q<Button>("clearButton");
            _clearButton.clicked += () =>
            {
                HandleClearPressed();
                DevSuiteUiUtils.ShowIconButtonClickedFeedback(_clearButton);
            };

            _scrollView = root.Q<ScrollView>("logsScrollView");
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            DevSuiteUiUtils.SetupScrollView(_scrollView);

            _topSpacer = new VisualElement { name = "logsTopSpacer" };
            _topSpacer.style.flexShrink = 0;
            _topSpacer.style.flexGrow = 0;
            _topSpacer.pickingMode = PickingMode.Ignore;
            _scrollView.Add(_topSpacer);

            _itemsContainer = new VisualElement { name = "logsItemsContainer" };
            _itemsContainer.style.flexShrink = 0;
            _itemsContainer.style.flexGrow = 0;
            _itemsContainer.style.flexDirection = FlexDirection.Column;
            _scrollView.Add(_itemsContainer);

            _bottomSpacer = new VisualElement { name = "logsBottomSpacer" };
            _bottomSpacer.style.flexShrink = 0;
            _bottomSpacer.style.flexGrow = 0;
            _bottomSpacer.pickingMode = PickingMode.Ignore;
            _scrollView.Add(_bottomSpacer);

            _scrollView.verticalScroller.valueChanged += _ =>
            {
                ClearHovers();
                UpdateVisibleItems();
            };
            _scrollView.RegisterCallback<GeometryChangedEvent>(_ => UpdateVisibleItems());
            _scrollView.RegisterCallback<PointerLeaveEvent>(_ => ClearHovers());

            _cliInputField = root.Q<TextField>("cliInputField");
            _cliGhostField = root.Q<TextField>("cliGhostField");
            _cliSendButton = root.Q<Button>("cliSendButton");
            _cliTooltipContainer = root.Q<VisualElement>("cliTooltipContainer");
            _cliTooltipScrollView = root.Q<ScrollView>("cliTooltipScrollView");
            DevSuiteUiUtils.SetupScrollView(_cliTooltipScrollView);

            if (_cliInputField != null)
            {
                _cliInputField.focusable = true;
                _cliInputField.tabIndex = -1;

                var textInput = _cliInputField.Q("unity-text-input");
                if (textInput != null)
                {
                    textInput.focusable = true;
                    textInput.tabIndex = -1;
                }
                else
                {
                    _cliInputField.RegisterCallback<AttachToPanelEvent>(
                        _ =>
                        {
                            var ti = _cliInputField.Q("unity-text-input");
                            if (ti != null)
                            {
                                ti.focusable = true;
                                ti.tabIndex = -1;
                            }
                        }
                    );
                }

                _cliInputField.RegisterCallback<PointerDownEvent>(
                    _ =>
                    {
                        _cliInputField.focusable = true;
                        var ti = _cliInputField.Q("unity-text-input");
                        if (ti != null)
                        {
                            ti.focusable = true;
                        }
                    }
                );

                _cliInputField.RegisterValueChangedCallback(evt => HandleCliInputChanged(evt.newValue));
                _cliInputField.RegisterCallback<FocusInEvent>(
                    _ =>
                    {
                        _cliInputFocused = true;
                        ShowCliTooltip();
                    }
                );
                _cliInputField.RegisterCallback<FocusOutEvent>(
                    _ =>
                    {
                        if (_isSendingCli)
                        {
                            return;
                        }
                        _cliInputFocused = false;
                        schedule.Execute(
                            () =>
                            {
                                if (!_cliInputFocused && !_isPointerOverTooltip)
                                {
                                    HideCliTooltip();
                                }
                            }
                        ).StartingIn(200);
                    }
                );
                _cliInputField.RegisterCallback<KeyDownEvent>(
                    evt =>
                    {
                        var isEnter = evt.keyCode == KeyCode.Return
                                      || evt.keyCode == KeyCode.KeypadEnter
                                      || evt.character == '\n'
                                      || evt.character == '\r';

                        var isTab = evt.keyCode == KeyCode.Tab
                                    || evt.character == '\t'
                                    || (int)evt.character == 9;

                        var isUp = evt.keyCode == KeyCode.UpArrow;
                        var isDown = evt.keyCode == KeyCode.DownArrow;

#if ENABLE_INPUT_SYSTEM
                    if (UnityEngine.InputSystem.Keyboard.current != null)
                    {
                        var kb = UnityEngine.InputSystem.Keyboard.current;
                        isEnter |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                        isTab |= kb.tabKey.wasPressedThisFrame;
                        isUp |= kb.upArrowKey.wasPressedThisFrame;
                        isDown |= kb.downArrowKey.wasPressedThisFrame;
                    }
#endif

                        if (isEnter)
                        {
                            evt.StopImmediatePropagation();
                            HandleCliSend();
                        }
                        else if (isUp)
                        {
                            if (NavigateCliHistory(-1))
                            {
                                evt.StopImmediatePropagation();
                            }
                        }
                        else if (isDown)
                        {
                            if (NavigateCliHistory(1))
                            {
                                evt.StopImmediatePropagation();
                            }
                        }
                        else if (isTab)
                        {
                            evt.StopImmediatePropagation();
#if UNITY_2023_2_OR_NEWER
                            _cliInputField.focusController?.IgnoreEvent(evt);
#else
                            evt.PreventDefault();
#endif
                            HandleCliTab();
                        }
                    },
                    TrickleDown.TrickleDown
                );

                _cliInputField.RegisterCallback<NavigationMoveEvent>(
                    evt =>
                    {
                        if (evt.direction == NavigationMoveEvent.Direction.Up
                            || evt.direction == NavigationMoveEvent.Direction.Down
                            || evt.direction == NavigationMoveEvent.Direction.Next)
                        {
                            evt.StopImmediatePropagation();
#if UNITY_2023_2_OR_NEWER
                            _cliInputField.focusController?.IgnoreEvent(evt);
#else
                            evt.PreventDefault();
#endif
                        }
                    },
                    TrickleDown.TrickleDown
                );
            }

            if (_cliSendButton != null)
            {
                _cliSendButton.clicked += HandleCliSend;
            }

            if (_cliTooltipContainer != null)
            {
                _cliTooltipContainer.RegisterCallback<PointerEnterEvent>(_ => _isPointerOverTooltip = true);
                _cliTooltipContainer.RegisterCallback<PointerLeaveEvent>(_ => _isPointerOverTooltip = false);
                var cliTooltipHeader = _cliTooltipContainer.Q<VisualElement>("cliTooltipHeader");
                cliTooltipHeader.RegisterCallback<PointerDownEvent>(evt => evt.StopImmediatePropagation());
            }

            DevSuiteUiUtils.SetupTooltips(this);
        }

        internal DevSuiteContext Context => _context;

        internal void ClearHovers()
        {
            for (var i = 0; i < _itemPool.Count; i++)
            {
                _itemPool[i].RemoveFromClassList("hover-active");
            }
        }

        internal float GetCachedHeight(LogMessageData msg)
        {
            return _expandedHeights.TryGetValue(msg, out var h) ? h : 0f;
        }

        internal void SetCachedHeight(LogMessageData msg, float h)
        {
            _expandedHeights[msg] = h;
            UpdateVisibleItems();
        }

        internal void OnItemExpandedChanged(LogMessageData msg, LogItemView itemView)
        {
            if (!msg.Expanded)
            {
                _expandedHeights.Remove(msg);
            }
            UpdateVisibleItems();
        }

        private float GetItemHeight(LogMessageData msg)
        {
            if (!msg.Expanded)
            {
                return RowHeight;
            }

            if (_expandedHeights.TryGetValue(msg, out var h) && h > 0f)
            {
                return h;
            }

            var lines = 1;
            if (!string.IsNullOrEmpty(msg.CallStack))
            {
                for (var i = 0; i < msg.CallStack.Length; i++)
                {
                    if (msg.CallStack[i] == '\n')
                    {
                        lines++;
                    }
                }
            }
            if (!string.IsNullOrEmpty(msg.Message))
            {
                for (var i = 0; i < msg.Message.Length; i++)
                {
                    if (msg.Message[i] == '\n')
                    {
                        lines++;
                    }
                }
            }
            return RowHeight + (lines * 16f) + 12f;
        }

        private float GetViewportHeight()
        {
            var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
            if (float.IsNaN(viewportHeight) || viewportHeight <= 0)
            {
                viewportHeight = _scrollView.resolvedStyle.height;
            }
            if (float.IsNaN(viewportHeight) || viewportHeight <= 0)
            {
                viewportHeight = 400f;
            }
            return viewportHeight;
        }

        private float GetTotalHeight()
        {
            if (_expandedHeights.Count == 0)
            {
                return _filteredMessages.Count * RowHeight;
            }

            var total = 0f;
            for (var i = 0; i < _filteredMessages.Count; i++)
            {
                total += GetItemHeight(_filteredMessages[i]);
            }
            return total;
        }

        private void UpdateVisibleItems()
        {
            if (_scrollView == null || _filteredMessages == null)
            {
                return;
            }

            var totalCount = _filteredMessages.Count;
            if (totalCount == 0)
            {
                _topSpacer.style.height = 0;
                _bottomSpacer.style.height = 0;
                for (var i = 0; i < _itemPool.Count; i++)
                {
                    _itemPool[i].style.display = DisplayStyle.None;
                    _itemPool[i].Unbind();
                }
                return;
            }

            var viewportHeight = GetViewportHeight();
            var totalHeight = GetTotalHeight();
            var maxScrollY = Mathf.Max(0f, totalHeight - viewportHeight);
            if (_scrollView.scrollOffset.y > maxScrollY)
            {
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, maxScrollY);
            }

            const int buffer = 3;
            var scrollY = Mathf.Max(0f, _scrollView.scrollOffset.y);

            int firstVisibleIndex;
            int lastVisibleIndex;
            float topHeight;
            float bottomHeight;

            if (_expandedHeights.Count == 0)
            {
                firstVisibleIndex = Mathf.Clamp(Mathf.FloorToInt(scrollY / RowHeight) - buffer, 0, totalCount - 1);
                var visibleCount = Mathf.CeilToInt(viewportHeight / RowHeight) + (buffer * 2);
                lastVisibleIndex = Mathf.Clamp(firstVisibleIndex + visibleCount - 1, 0, totalCount - 1);
                topHeight = firstVisibleIndex * RowHeight;
                bottomHeight = Mathf.Max(0f, (totalCount - 1 - lastVisibleIndex) * RowHeight);
            }
            else
            {
                var targetMinY = Mathf.Max(0f, scrollY - (buffer * RowHeight));
                var currentY = 0f;
                firstVisibleIndex = 0;
                topHeight = 0f;
                var idx = 0;
                while (idx < totalCount)
                {
                    var h = GetItemHeight(_filteredMessages[idx]);
                    if (currentY + h >= targetMinY)
                    {
                        firstVisibleIndex = idx;
                        topHeight = currentY;
                        break;
                    }
                    currentY += h;
                    idx++;
                }
                if (idx >= totalCount)
                {
                    firstVisibleIndex = Mathf.Max(0, totalCount - 1);
                    topHeight = currentY;
                }

                var targetMaxY = scrollY + viewportHeight + (buffer * RowHeight);
                lastVisibleIndex = firstVisibleIndex;
                var runningY = topHeight;
                for (var j = firstVisibleIndex; j < totalCount; j++)
                {
                    lastVisibleIndex = j;
                    runningY += GetItemHeight(_filteredMessages[j]);
                    if (runningY >= targetMaxY)
                    {
                        break;
                    }
                }

                var remainingY = 0f;
                for (var j = lastVisibleIndex + 1; j < totalCount; j++)
                {
                    remainingY += GetItemHeight(_filteredMessages[j]);
                }
                bottomHeight = remainingY;
            }

            var countToDisplay = lastVisibleIndex - firstVisibleIndex + 1;

            while (_itemPool.Count < countToDisplay)
            {
                var item = new LogItemView(this);
                _itemPool.Add(item);
                _itemsContainer.Add(item);
            }

            _topSpacer.style.height = topHeight;
            _bottomSpacer.style.height = bottomHeight;

            for (var i = 0; i < _itemPool.Count; i++)
            {
                var item = _itemPool[i];
                if (i < countToDisplay)
                {
                    var msgIndex = firstVisibleIndex + i;
                    item.style.display = DisplayStyle.Flex;
                    item.Bind(_filteredMessages[msgIndex], msgIndex == totalCount - 1);
                }
                else
                {
                    item.style.display = DisplayStyle.None;
                    item.Unbind();
                }
            }
        }

        private bool MatchesFilter(LogMessageData msg)
        {
            if (_context == null)
            {
                return false;
            }

            if (_context.HiddenLogSeverity.Contains(msg.Level))
            {
                return false;
            }

            var filterRegex = _context.LogsFilterRegex;
            if (filterRegex == null)
            {
                return true;
            }

            return filterRegex.IsMatch(msg.Message) || (msg.CallStack != null && filterRegex.IsMatch(msg.CallStack));
        }

        public void Initialize(DevSuiteContext context)
        {
            Reset();
            _context = context;

            _context.OnLogMessagesChanged += HandleLogMessagesChanged;
            _context.OnLogMessagesMessageAdded += HandleLogMessagesMessageAdded;
            _context.OnLogMessagesVisibilityChanged += HandleLogMessagesVisibilityChanged;
            _context.OnFocusCliRequested += FocusCliInput;

            _copyButton.text = "\ue09e";
            _saveButton.text = "\ue14d";
            _folderButton.text = "\ue247";
            _clearButton.text = "\ue18e";

            _filterField.SetValueWithoutNotify(_context.LogsPattern);
            UpdateClearFilterButton();
            UpdateView();
        }

        public void FocusCliInput()
        {
            void DoFocus()
            {
                if (_cliInputField != null)
                {
                    _cliInputField.focusable = true;
                    var input = _cliInputField.Q("unity-text-input");
                    if (input != null)
                    {
                        input.focusable = true;
                        input.Focus();
                    }
                    else
                    {
                        _cliInputField.Focus();
                    }
                    var len = _cliInputField.value?.Length ?? 0;
                    _cliInputField.SelectRange(len, len);
                    _cliInputFocused = true;
                    ShowCliTooltip();
                }
            }

            DoFocus();
            schedule.Execute(DoFocus).StartingIn(50);
        }

        private void UpdateSeverityButtons()
        {
            _ordinaryCountLabel.text = _ordinaryCount.ToString();
            _warningCountLabel.text = _warningCount.ToString();
            _errorCountLabel.text = _errorCount.ToString();

            if (_context == null)
            {
                return;
            }

            _ordinaryButton.EnableInClassList("active", !_context.HiddenLogSeverity.Contains(GeneralizedLogSeverity.Ordinary));
            _warningButton.EnableInClassList("active", !_context.HiddenLogSeverity.Contains(GeneralizedLogSeverity.Warning));
            _errorButton.EnableInClassList("active", !_context.HiddenLogSeverity.Contains(GeneralizedLogSeverity.Error));

            _regexButton.EnableInClassList("active", _context.LogsRegex);
            if (_context.LogsRegex && _context.LogsFilterRegex == DevSuiteUtils.NeverMatch)
            {
                _regexButton.style.color = new Color(0.9f, 0.9f, 0.9f);
            }
            else
            {
                _regexButton.style.color = StyleKeyword.Null; // Reset to CSS
            }
        }

        public void Reset()
        {
            if (_context != null)
            {
                _context.OnLogMessagesMessageAdded -= HandleLogMessagesMessageAdded;
                _context.OnLogMessagesChanged -= HandleLogMessagesChanged;
                _context.OnLogMessagesVisibilityChanged -= HandleLogMessagesVisibilityChanged;
                _context.OnFocusCliRequested -= FocusCliInput;
                _context = null;
            }
            _cliInputField?.SetValueWithoutNotify(string.Empty);
            _cliGhostField?.SetValueWithoutNotify(string.Empty);
            HideCliTooltip();
            _filteredMessages.Clear();
            _expandedHeights.Clear();
            _topSpacer.style.height = 0;
            _bottomSpacer.style.height = 0;
            for (var i = 0; i < _itemPool.Count; i++)
            {
                _itemPool[i].style.display = DisplayStyle.None;
                _itemPool[i].Unbind();
            }
            _ordinaryCount = 0;
            _warningCount = 0;
            _errorCount = 0;
            UpdateSeverityButtons();
        }

        private bool _isSendingCli;
        private int _lastSendFrame = -1;
        private float _lastSendTime = -1f;
        private int _lastTabFrame = -1;
        private float _lastTabTime = -1f;
        private int _lastHistoryFrame = -1;
        private float _lastHistoryTime = -1f;

        private void HandleCliSend()
        {
            if (_context == null || _cliInputField == null)
            {
                return;
            }

            var currentFrame = Time.frameCount;
            var currentTime = Time.realtimeSinceStartup;
            if (_lastSendFrame == currentFrame && Math.Abs(currentTime - _lastSendTime) < 0.05f)
            {
                return;
            }
            _lastSendFrame = currentFrame;
            _lastSendTime = currentTime;

            _cliHistoryIndex = -1;
            _cliDraftText = string.Empty;

            var text = _cliInputField.value;
            if (text != null)
            {
                text = text.TrimEnd('\r', '\n');
            }
            _isSendingCli = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _context.ExecuteCliCommand(text);
                }
                _cliInputField.SetValueWithoutNotify(string.Empty);
                _cliGhostField?.SetValueWithoutNotify(string.Empty);
                HideCliTooltip();
            }
            finally
            {
                _isSendingCli = false;
            }

            FocusCliInput();
        }

        private bool NavigateCliHistory(int direction)
        {
            if (_context == null || _cliInputField == null)
            {
                return false;
            }

            var currentFrame = Time.frameCount;
            var currentTime = Time.realtimeSinceStartup;
            if (_lastHistoryFrame == currentFrame && Math.Abs(currentTime - _lastHistoryTime) < 0.05f)
            {
                return true;
            }
            _lastHistoryFrame = currentFrame;
            _lastHistoryTime = currentTime;

            var history = _context.GetCliCommandHistory();
            if (history == null || history.Count == 0)
            {
                return false;
            }

            if (direction < 0) // UpArrow: older command
            {
                if (_cliHistoryIndex == -1)
                {
                    _cliDraftText = _cliInputField.value ?? string.Empty;
                    _cliHistoryIndex = history.Count - 1;
                }
                else if (_cliHistoryIndex > 0)
                {
                    _cliHistoryIndex--;
                }
                else
                {
                    return true;
                }

                SetCliInputFromHistory(history[_cliHistoryIndex]);
                return true;
            }
            else if (direction > 0) // DownArrow: newer command
            {
                if (_cliHistoryIndex == -1)
                {
                    return false;
                }

                if (_cliHistoryIndex < history.Count - 1)
                {
                    _cliHistoryIndex++;
                    SetCliInputFromHistory(history[_cliHistoryIndex]);
                }
                else
                {
                    _cliHistoryIndex = -1;
                    SetCliInputFromHistory(_cliDraftText);
                }
                return true;
            }

            return false;
        }

        private void SetCliInputFromHistory(string text)
        {
            if (_cliInputField == null)
            {
                return;
            }

            _cliInputField.value = text;
            _cliInputField.SelectRange(text.Length, text.Length);
            UpdateCliGhost(text);
            UpdateCliTooltip(text);
        }

        internal void HandleCliTab()
        {
            if (_context == null || _cliInputField == null)
            {
                return;
            }

            var currentFrame = Time.frameCount;
            var currentTime = Time.realtimeSinceStartup;
            if (_lastTabFrame == currentFrame && Math.Abs(currentTime - _lastTabTime) < 0.05f)
            {
                return;
            }
            _lastTabFrame = currentFrame;
            _lastTabTime = currentTime;

            var currentText = _cliInputField.value ?? string.Empty;
            var allCommands = _context.GetActiveCliCommands();
            if (DevSuiteUtils.TryGetCliTabCompletion(currentText, allCommands, out var completedText))
            {
                _cliInputField.value = completedText;
                _cliInputField.SelectRange(completedText.Length, completedText.Length);
                UpdateCliGhost(completedText);
                UpdateCliTooltip(completedText);
            }

            FocusCliInput();
        }

        private void HandleCliInputChanged(string newText)
        {
            if (newText != null && (newText.Contains('\n') || newText.Contains('\r')))
            {
                var cleaned = newText.Replace("\r", "").Replace("\n", "");
                _cliInputField.SetValueWithoutNotify(cleaned);
                newText = cleaned;
            }
            UpdateCliGhost(newText);
            if (_cliInputFocused)
            {
                UpdateCliTooltip(newText);
            }
        }

        private void ShowCliTooltip()
        {
            if (_cliInputField != null)
            {
                UpdateCliTooltip(_cliInputField.value);
            }
        }

        private void HideCliTooltip()
        {
            if (_cliTooltipContainer != null)
            {
                _cliTooltipContainer.style.display = DisplayStyle.None;
            }
        }

        private void UpdateCliTooltip(string currentText)
        {
            if (_context == null || _cliTooltipContainer == null || _cliTooltipScrollView == null)
            {
                return;
            }

            var allCommands = _context.GetActiveCliCommands();
            if (allCommands == null || allCommands.Count == 0)
            {
                HideCliTooltip();
                return;
            }

            var trimmed = (currentText ?? "").TrimStart();
            var spaceIdx = trimmed.IndexOf(' ');

            CliCommandData ghostCmd = null;
            if (!string.IsNullOrEmpty(trimmed))
            {
                if (spaceIdx < 0)
                {
                    var cmdQuery = trimmed;
                    ghostCmd = allCommands.FirstOrDefault(c => string.Equals(c.CliCommand, cmdQuery, StringComparison.OrdinalIgnoreCase))
                               ?? allCommands.FirstOrDefault(c => c.CliCommand.StartsWith(cmdQuery, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var cmdName = trimmed.Substring(0, spaceIdx);
                    ghostCmd = allCommands.FirstOrDefault(c => string.Equals(c.CliCommand, cmdName, StringComparison.OrdinalIgnoreCase));
                }
            }

            var cmdQueryStr = spaceIdx >= 0 ? trimmed.Substring(0, spaceIdx) : trimmed;

            List<CliCommandData> matches;
            if (string.IsNullOrEmpty(cmdQueryStr))
            {
                matches = allCommands
                    .OrderBy(x => ghostCmd != null && x == ghostCmd ? 0 : 1)
                    .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CliCommand, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(x => x.Priority)
                    .ToList();
            }
            else
            {
                var smartRegex = DevSuiteUtils.GetSmartSearchRegex(cmdQueryStr);
                matches = allCommands
                    .Select(
                        c =>
                        {
                            var rank = int.MaxValue;
                            if (ghostCmd != null && c == ghostCmd)
                            {
                                rank = -1;
                            }
                            else if (string.Equals(c.CliCommand, cmdQueryStr, StringComparison.OrdinalIgnoreCase))
                            {
                                rank = 0;
                            }
                            else if (c.CliCommand.StartsWith(cmdQueryStr, StringComparison.OrdinalIgnoreCase))
                            {
                                rank = 1;
                            }
                            else if (c.CliCommand.IndexOf(cmdQueryStr, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                rank = 2;
                            }
                            else if (smartRegex.IsMatch(c.CliCommand))
                            {
                                rank = 3;
                            }
                            else
                            {
                                var fullPath = $"{c.Path}/{c.CliCommand}";
                                if (fullPath.IndexOf(cmdQueryStr, StringComparison.OrdinalIgnoreCase) >= 0 || smartRegex.IsMatch(fullPath))
                                {
                                    rank = 4;
                                }
                                else if (!string.IsNullOrEmpty(c.Description) && c.Description.IndexOf(cmdQueryStr, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    rank = 5;
                                }
                            }
                            return (cmd: c, rank: rank);
                        }
                    )
                    .Where(x => x.rank < int.MaxValue)
                    .OrderBy(x => x.rank)
                    .ThenBy(x => x.cmd.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.cmd.CliCommand, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(x => x.cmd.Priority)
                    .Select(x => x.cmd)
                    .ToList();

                if (ghostCmd != null && !matches.Contains(ghostCmd))
                {
                    matches.Insert(0, ghostCmd);
                }
            }

            if (matches.Count == 0)
            {
                HideCliTooltip();
                return;
            }

            _cliTooltipScrollView.Clear();
            foreach (var cmd in matches)
            {
                var isGhost = ghostCmd != null && cmd == ghostCmd;
                var item = CreateCliSuggestionItem(cmd, isGhost);
                _cliTooltipScrollView.Add(item);
            }

            _cliTooltipScrollView.scrollOffset = Vector2.zero;
            _cliTooltipContainer.style.display = DisplayStyle.Flex;
        }

        private VisualElement CreateCliSuggestionItem(CliCommandData cmd, bool isHighlighted = false)
        {
            var item = new VisualElement();
            item.AddToClassList("logs-cli-tooltip-item");
            if (isHighlighted)
            {
                item.AddToClassList("highlighted");
            }

            var header = new VisualElement();
            header.AddToClassList("logs-cli-tooltip-header");

            var pathPrefix = $"{cmd.Path}/";
            var pathLabel = new Label(pathPrefix);
            pathLabel.AddToClassList("logs-cli-tooltip-path");
            header.Add(pathLabel);

            var cmdLabel = new Label(cmd.CliCommand);
            cmdLabel.AddToClassList("logs-cli-tooltip-cmd");
            header.Add(cmdLabel);

            var paramStr = FormatParameters(cmd);
            if (!string.IsNullOrEmpty(paramStr))
            {
                var paramsLabel = new Label(paramStr);
                paramsLabel.AddToClassList("logs-cli-tooltip-params");
                header.Add(paramsLabel);
            }
            item.Add(header);

            if (!string.IsNullOrEmpty(cmd.Description))
            {
                var descLabel = new Label(cmd.Description);
                descLabel.AddToClassList("logs-cli-tooltip-desc");
                item.Add(descLabel);
            }

            item.RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    evt.StopImmediatePropagation();
                    if (_cliInputField != null)
                    {
                        _cliHistoryIndex = -1;
                        _cliDraftText = string.Empty;
                        var pastedText = cmd.CliCommand + " ";
                        _cliInputField.value = pastedText;
                        _cliInputField.focusable = true;
                        var textInput = _cliInputField.Q("unity-text-input");
                        if (textInput != null)
                        {
                            textInput.focusable = true;
                            textInput.Focus();
                        }
                        else
                        {
                            _cliInputField.Focus();
                        }
                        _cliInputField.SelectRange(pastedText.Length, pastedText.Length);
                        _cliInputFocused = true;
                        UpdateCliGhost(pastedText);
                        UpdateCliTooltip(pastedText);

                        schedule.Execute(
                            () =>
                            {
                                if (_cliInputField != null)
                                {
                                    _cliInputField.focusable = true;
                                    var input = _cliInputField.Q("unity-text-input");
                                    if (input != null)
                                    {
                                        input.focusable = true;
                                        input.Focus();
                                    }
                                    else
                                    {
                                        _cliInputField.Focus();
                                    }
                                    _cliInputField.SelectRange(pastedText.Length, pastedText.Length);
                                    _cliInputFocused = true;
                                }
                            }
                        );
                    }
                }
            );

            return item;
        }

        private void UpdateCliGhost(string currentText)
        {
            if (_cliGhostField == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(currentText) || _context == null)
            {
                _cliGhostField.SetValueWithoutNotify(string.Empty);
                return;
            }

            var allCommands = _context.GetActiveCliCommands();
            if (allCommands == null || allCommands.Count == 0)
            {
                _cliGhostField.SetValueWithoutNotify(string.Empty);
                return;
            }

            var trimmed = currentText.TrimStart();
            var spaceIdx = trimmed.IndexOf(' ');

            if (spaceIdx < 0)
            {
                var cmdQuery = trimmed;
                var matched = allCommands.FirstOrDefault(c => string.Equals(c.CliCommand, cmdQuery, StringComparison.OrdinalIgnoreCase))
                              ?? allCommands.FirstOrDefault(c => c.CliCommand.StartsWith(cmdQuery, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                {
                    var remainingCmd = matched.CliCommand.Length > cmdQuery.Length
                        ? matched.CliCommand.Substring(cmdQuery.Length)
                        : string.Empty;

                    var paramPlaceholders = FormatParameterPlaceholders(matched.Parameters, 0);
                    var ghostSuffix = remainingCmd;
                    if (!string.IsNullOrEmpty(paramPlaceholders))
                    {
                        ghostSuffix += (string.IsNullOrEmpty(ghostSuffix) ? " " : " ") + paramPlaceholders;
                    }

                    _cliGhostField.SetValueWithoutNotify(currentText + ghostSuffix);
                }
                else
                {
                    _cliGhostField.SetValueWithoutNotify(currentText);
                }
            }
            else
            {
                var cmdName = trimmed.Substring(0, spaceIdx);
                var matched = allCommands.FirstOrDefault(c => string.Equals(c.CliCommand, cmdName, StringComparison.OrdinalIgnoreCase));

                if (matched != null && matched.Parameters != null && matched.Parameters.Count > 0)
                {
                    var tokens = DevSuiteUtils.TokenizeCommandLine(trimmed);
                    var userArgsCount = Math.Max(0, tokens.Count - 1);
                    var endsWithSpace = currentText.EndsWith(" ");

                    var nextParamIdx = userArgsCount;
                    var remainingPlaceholders = FormatParameterPlaceholders(matched.Parameters, nextParamIdx);
                    if (!string.IsNullOrEmpty(remainingPlaceholders))
                    {
                        var separator = endsWithSpace ? string.Empty : " ";
                        _cliGhostField.SetValueWithoutNotify(currentText + separator + remainingPlaceholders);
                    }
                    else
                    {
                        _cliGhostField.SetValueWithoutNotify(currentText);
                    }
                }
                else
                {
                    _cliGhostField.SetValueWithoutNotify(currentText);
                }
            }
        }

        private static string FormatParameterPlaceholders(IReadOnlyList<CommandUnitButtonParameter> parameters, int startIndex)
        {
            if (parameters == null || startIndex >= parameters.Count)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            for (var i = startIndex; i < parameters.Count; i++)
            {
                var p = parameters[i];
                var typeName = DevSuiteUtils.GetFriendlyTypeName(p.Type);
                parts.Add($"<{typeName} {p.ParameterName}>");
            }
            return string.Join(" ", parts);
        }

        private static string FormatParameters(CliCommandData cmd)
        {
            if (cmd.Parameters == null || cmd.Parameters.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                var p = cmd.Parameters[i];
                var typeName = DevSuiteUtils.GetFriendlyTypeName(p.Type);
                var val = p.GetValue?.Invoke();
                var valStr = val != null ? val is string s ? $"\"{s}\"" : val.ToString() : null;

                if (!string.IsNullOrEmpty(valStr))
                {
                    parts.Add($"<{typeName} {p.ParameterName} = {valStr}>");
                }
                else
                {
                    parts.Add($"<{typeName} {p.ParameterName}>");
                }
            }
            return string.Join(" ", parts);
        }

        private void HandleLogMessagesChanged()
        {
            UpdateView();
        }

        private void HandleLogMessagesVisibilityChanged()
        {
            UpdateSeverityButtons();
            UpdateVisibility();

            var focused = _filterField.focusController?.focusedElement as VisualElement;
            if (focused == null || !_filterField.Contains(focused))
            {
                _filterField.SetValueWithoutNotify(_context.LogsPattern);
            }
            UpdateClearFilterButton();
        }

        private bool _isHandlingMessage;

        private void HandleLogMessagesMessageAdded(LogMessageData message)
        {
            if (_isHandlingMessage)
            {
                return;
            }

            _isHandlingMessage = true;
            try
            {
                if (message.Level == GeneralizedLogSeverity.Ordinary)
                {
                    _ordinaryCount++;
                }
                else if (message.Level == GeneralizedLogSeverity.Warning)
                {
                    _warningCount++;
                }
                else if (message.Level == GeneralizedLogSeverity.Error)
                {
                    _errorCount++;
                }

                UpdateSeverityButtons();

                // Capture scroll state before adding the new element
                var scroller = _scrollView.verticalScroller;
                var wasAtBottom = scroller.highValue <= 0 || scroller.value >= scroller.highValue - 1f;

                if (MatchesFilter(message))
                {
                    _filteredMessages.Add(message);
                    if (wasAtBottom)
                    {
                        var maxScroll = Mathf.Max(0f, GetTotalHeight() - GetViewportHeight());
                        _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, maxScroll);
                    }
                    UpdateVisibleItems();
                }
            }
            catch (InvalidOperationException)
            {
                // Prevent cascading UI exceptions when a log occurs during visual tree rendering or layout pass
            }
            finally
            {
                _isHandlingMessage = false;
            }
        }

        private void HandleTextChanged(string newText)
        {
            if (_context == null)
            {
                return;
            }

            if (newText != null && (newText.Contains('\n') || newText.Contains('\r')))
            {
                var cleaned = newText.Replace("\r", " ").Replace("\n", " ");
                _filterField.SetValueWithoutNotify(cleaned);
                newText = cleaned;
            }

            _context.LogsPattern = newText;
            UpdateClearFilterButton();
        }

        private void UpdateClearFilterButton()
        {
            var hasText = !string.IsNullOrEmpty(_filterField?.value);
            _clearFilterBtn.style.display = hasText ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleRegexPressed()
        {
            if (_context == null)
            {
                return;
            }

            _context.LogsRegex = !_context.LogsRegex;
        }

        private void HandleSeverityClick(GeneralizedLogSeverity severity)
        {
            if (_context == null)
            {
                return;
            }

            var currentHidden = _context.HiddenLogSeverity;
            if (!currentHidden.Add(severity))
            {
                currentHidden.Remove(severity);
            }
            _context.HiddenLogSeverity = currentHidden; //needed to call saving
        }

        private void HandleClearPressed()
        {
            if (_context == null)
            {
                return;
            }

            _context.ClearLogs();
            _filterField.value = "";
            HandleTextChanged("");
        }

        private void HandleCopyPressed()
        {
            DevSuiteUtils.CopyToClipboard(_context.GetAllLogsText(), _context);
            Debug.Log("Copied the filtered log into the clipboard");
        }

        private void HandleSavePressed()
        {
            var folderPath = GetLogsFolderPath();
            var filePath = Path.Combine(folderPath, $"Log_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.WriteAllText(filePath, _context.GetAllLogsText());
            Debug.Log($"Saved the filtered log into {filePath}");
        }

        private void HandleFolderPressed()
        {
            var folderPath = GetLogsFolderPath();
            Application.OpenURL($"file://{folderPath}");
        }

        private string GetLogsFolderPath()
        {
            var folderPath = Path.Combine(Application.persistentDataPath, SaveFolderPath);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            return folderPath;
        }

        private void UpdateView()
        {
            if (_context == null)
            {
                return;
            }

            var allMessages = _context.AllLogMessages;
            _filteredMessages.Clear();
            _expandedHeights.Clear();
            _ordinaryCount = 0;
            _warningCount = 0;
            _errorCount = 0;

            foreach (var m in allMessages)
            {
                if (m.Level == GeneralizedLogSeverity.Ordinary)
                {
                    _ordinaryCount++;
                }
                else if (m.Level == GeneralizedLogSeverity.Warning)
                {
                    _warningCount++;
                }
                else if (m.Level == GeneralizedLogSeverity.Error)
                {
                    _errorCount++;
                }

                if (MatchesFilter(m))
                {
                    _filteredMessages.Add(m);
                }
            }

            UpdateSeverityButtons();
            UpdateVisibleItems();
        }

        private void UpdateVisibility()
        {
            if (_context == null)
            {
                return;
            }

            var allMessages = _context.AllLogMessages;
            _filteredMessages.Clear();
            _expandedHeights.Clear();

            foreach (var m in allMessages)
            {
                if (MatchesFilter(m))
                {
                    _filteredMessages.Add(m);
                }
            }

            UpdateVisibleItems();
        }
    }

    internal class LogItemView : VisualElement
    {
        private readonly LogsPanelView _owner;
        private readonly VisualElement _header;
        private readonly Label _timeLabel;
        private readonly Label _messageLabel;
        private readonly Button _copyBtn;
        private readonly Label _callStackLabel;

        private LogMessageData _boundMsg;

        public LogItemView(LogsPanelView owner)
        {
            _owner = owner;
            name = "logItemContainer";
            AddToClassList("log-item-container");

            _header = new VisualElement { name = "logItemHeader" };
            _header.AddToClassList("log-item-header");

            _timeLabel = new Label { name = "timeLabel" };
            _timeLabel.AddToClassList("log-item-time");
            _header.Add(_timeLabel);

            _messageLabel = new Label { name = "messageLabel" };
            _messageLabel.AddToClassList("log-item-message");
            _header.Add(_messageLabel);

            _copyBtn = new Button
            {
                name = "copyBtn",
                text = "\ue09e",
                tooltip = "Copy log message to clipboard.\n\n<b><i>Hint: </i></b>Customize copy behavior via <b><color=#ffc800>DevSuiteContext.Default.CopyToClipboardAction</color></b>.",
            };
            _copyBtn.AddToClassList("log-item-copy-btn");
            _copyBtn.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopImmediatePropagation();
                if (_boundMsg != null && _owner.Context != null)
                {
                    DevSuiteUtils.CopyToClipboard(_boundMsg.MessageAndCallStack(), _owner.Context);
                    DevSuiteUiUtils.ShowIconButtonClickedFeedback(_copyBtn);
                    Debug.Log("Copied the message into the clipboard");
                }
            });
            _header.Add(_copyBtn);

            _callStackLabel = new Label { name = "callStackLabel" };
            _callStackLabel.AddToClassList("log-item-callstack");

            Add(_header);
            Add(_callStackLabel);

            _messageLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (_boundMsg == null)
                {
                    return;
                }
                _boundMsg.Expanded = !_boundMsg.Expanded;
                _owner.OnItemExpandedChanged(_boundMsg, this);
            });

            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!ClassListContains("hover-active"))
                {
                    _owner.ClearHovers();
                    AddToClassList("hover-active");
                }
            });

            RegisterCallback<PointerLeaveEvent>(evt => RemoveFromClassList("hover-active"));

            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (_boundMsg != null && _boundMsg.Expanded)
                {
                    var h = evt.newRect.height;
                    if (h > 0 && Math.Abs(h - _owner.GetCachedHeight(_boundMsg)) > 1f)
                    {
                        _owner.SetCachedHeight(_boundMsg, h);
                    }
                }
            });
        }

        public void Bind(LogMessageData msg, bool isLast)
        {
            _boundMsg = msg;
            userData = msg;

            _timeLabel.text = $"<mspace=7>[{msg.Timestamp:HH:mm:ss.fff}]</mspace>";

            var color = msg.Level switch
            {
                GeneralizedLogSeverity.Ordinary => new Color(0.5f, 0.8f, 1f),
                GeneralizedLogSeverity.Warning => new Color(1f, 0.8f, 0f),
                GeneralizedLogSeverity.Error => new Color(1f, 0.4f, 0.4f),
                _ => Color.white,
            };
            _messageLabel.style.color = color;

            EnableInClassList("expanded", msg.Expanded);
            if (msg.Expanded)
            {
                _messageLabel.text = msg.Message;
                _messageLabel.style.whiteSpace = WhiteSpace.Normal;
                _callStackLabel.text = msg.CallStack;
                _callStackLabel.style.display = !string.IsNullOrEmpty(msg.CallStack) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                var firstLineEnd = msg.Message?.IndexOf('\n') ?? -1;
                _messageLabel.text = firstLineEnd >= 0 ? msg.Message.Substring(0, firstLineEnd).TrimEnd('\r') : msg.Message;
                _messageLabel.style.whiteSpace = WhiteSpace.NoWrap;
                _callStackLabel.text = string.Empty;
                _callStackLabel.style.display = DisplayStyle.None;
            }

            EnableInClassList("last-item", isLast);
            RemoveFromClassList("hover-active");
        }

        public void Unbind()
        {
            _boundMsg = null;
            userData = null;
            RemoveFromClassList("hover-active");
            RemoveFromClassList("expanded");
            RemoveFromClassList("last-item");
        }
    }
}