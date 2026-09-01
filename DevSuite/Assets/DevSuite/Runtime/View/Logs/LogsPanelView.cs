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
        private readonly List<VisualElement> _allMessageElements = new();

        private readonly TextField _filterField;
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

        private readonly HashSet<GeneralizedLogSeverity> _collectStackTraceFor = new() { GeneralizedLogSeverity.Warning, GeneralizedLogSeverity.Error };

        private const string SaveFolderPath = "logger_panel";

        public LogsPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            style.flexGrow = 1;
            AddToClassList("ff-panel");

            var root = this.Q<VisualElement>("logs-panel-root") ?? this;

            _filterField = root.Q<TextField>("filterField");
            DevSuiteUtils.SetupInputFieldFocus(_filterField);
            _filterField.RegisterValueChangedCallback(evt => HandleTextChanged(evt.newValue));
            _filterField.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (_context != null)
                    _filterField.SetValueWithoutNotify(_context.LogsPattern);
            });

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
            _copyButton.clicked += () => { HandleCopyPressed(); DevSuiteUtils.ShowIconButtonClickedFeedback(_copyButton); };

            _saveButton = root.Q<Button>("saveButton");
            _saveButton.clicked += () => { HandleSavePressed(); DevSuiteUtils.ShowIconButtonClickedFeedback(_saveButton); };

            _folderButton = root.Q<Button>("folderButton");
            _folderButton.clicked += () => { HandleFolderPressed(); DevSuiteUtils.ShowIconButtonClickedFeedback(_folderButton); };

            _clearButton = root.Q<Button>("clearButton");
            _clearButton.clicked += () => { HandleClearPressed(); DevSuiteUtils.ShowIconButtonClickedFeedback(_clearButton); };

            _scrollView = root.Q<ScrollView>("logsScrollView");
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scrollView.verticalScroller.valueChanged += _ => ClearHovers();

            _cliInputField = root.Q<TextField>("cliInputField");
            _cliGhostField = root.Q<TextField>("cliGhostField");
            _cliSendButton = root.Q<Button>("cliSendButton");
            _cliTooltipContainer = root.Q<VisualElement>("cliTooltipContainer");
            _cliTooltipScrollView = root.Q<ScrollView>("cliTooltipScrollView");

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
                    _cliInputField.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var ti = _cliInputField.Q("unity-text-input");
                        if (ti != null)
                        {
                            ti.focusable = true;
                            ti.tabIndex = -1;
                        }
                    });
                }

                _cliInputField.RegisterCallback<PointerDownEvent>(_ =>
                {
                    _cliInputField.focusable = true;
                    var ti = _cliInputField.Q("unity-text-input");
                    if (ti != null)
                    {
                        ti.focusable = true;
                    }
                });

                _cliInputField.RegisterValueChangedCallback(evt => HandleCliInputChanged(evt.newValue));
                _cliInputField.RegisterCallback<FocusInEvent>(_ =>
                {
                    _cliInputFocused = true;
                    ShowCliTooltip();
                });
                _cliInputField.RegisterCallback<FocusOutEvent>(_ =>
                {
                    if (_isSendingCli)
                    {
                        return;
                    }
                    _cliInputFocused = false;
                    schedule.Execute(() =>
                    {
                        if (!_cliInputFocused && !_isPointerOverTooltip)
                        {
                            HideCliTooltip();
                        }
                    }).StartingIn(200);
                });
                _cliInputField.RegisterCallback<KeyDownEvent>(evt =>
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
                        evt.PreventDefault();
                        HandleCliSend();
                    }
                    else if (isUp)
                    {
                        if (NavigateCliHistory(-1))
                        {
                            evt.StopImmediatePropagation();
                            evt.PreventDefault();
                        }
                    }
                    else if (isDown)
                    {
                        if (NavigateCliHistory(1))
                        {
                            evt.StopImmediatePropagation();
                            evt.PreventDefault();
                        }
                    }
                    else if (isTab)
                    {
                        evt.StopImmediatePropagation();
                        evt.PreventDefault();
                        HandleCliTab();
                    }
                }, TrickleDown.TrickleDown);

                _cliInputField.RegisterCallback<NavigationSubmitEvent>(evt =>
                {
                    evt.StopImmediatePropagation();
                    evt.PreventDefault();
                    HandleCliSend();
                }, TrickleDown.TrickleDown);

                _cliInputField.RegisterCallback<NavigationMoveEvent>(evt =>
                {
                    if (evt.direction == NavigationMoveEvent.Direction.Up)
                    {
                        if (NavigateCliHistory(-1))
                        {
                            evt.StopImmediatePropagation();
                            evt.PreventDefault();
                        }
                    }
                    else if (evt.direction == NavigationMoveEvent.Direction.Down)
                    {
                        if (NavigateCliHistory(1))
                        {
                            evt.StopImmediatePropagation();
                            evt.PreventDefault();
                        }
                    }
                    else if (evt.direction == NavigationMoveEvent.Direction.Next)
                    {
                        evt.StopImmediatePropagation();
                        evt.PreventDefault();
                        HandleCliTab();
                    }
                }, TrickleDown.TrickleDown);
            }

            if (_cliSendButton != null)
            {
                _cliSendButton.clicked += HandleCliSend;
            }

            if (_cliTooltipContainer != null)
            {
                _cliTooltipContainer.RegisterCallback<PointerEnterEvent>(_ => _isPointerOverTooltip = true);
                _cliTooltipContainer.RegisterCallback<PointerLeaveEvent>(_ => _isPointerOverTooltip = false);
            }

            DevSuiteUtils.SetupTooltips(this);
        }

        private void ClearHovers()
        {
            foreach (var el in _allMessageElements)
            {
                el.RemoveFromClassList("hover-active");
            }
        }

        private VisualElement CreateLogItem(LogMessageData msg)
        {
            var element = new VisualElement { name = "logItemContainer" };
            element.AddToClassList("log-item-container");
            element.userData = msg;

            var header = new VisualElement { name = "logItemHeader" };
            header.AddToClassList("log-item-header");

            var messageLabel = new Label { name = "messageLabel" };
            messageLabel.AddToClassList("log-item-message");

            void UpdateExpandedState()
            {
                if (msg.Expanded)
                {
                    messageLabel.text = msg.Message;
                }
                else
                {
                    var firstLineEnd = msg.Message?.IndexOf('\n') ?? -1;
                    messageLabel.text = firstLineEnd >= 0 ? msg.Message.Substring(0, firstLineEnd).TrimEnd('\r') : msg.Message;
                }
            }
            UpdateExpandedState();

            var color = msg.Level switch
            {
                GeneralizedLogSeverity.Ordinary => new Color(0.5f, 0.8f, 1f),
                GeneralizedLogSeverity.Warning => new Color(1f, 0.8f, 0f),
                GeneralizedLogSeverity.Error => new Color(1f, 0.4f, 0.4f),
                _ => Color.white
            };
            messageLabel.style.color = color;

            var copyBtn = new Button { name = "copyBtn", text = "\uf0c5" };
            copyBtn.AddToClassList("log-item-copy-btn");
            header.Add(copyBtn);

            copyBtn.RegisterCallback<ClickEvent>(evt =>
            {
                DevSuiteUtils.CopyToClipboard(msg.MessageAndCallStack());
                DevSuiteUtils.ShowIconButtonClickedFeedback(copyBtn);
                Debug.Log("Copied the message into the clipboard");
            });

            var timeLabel = new Label { name = "timeLabel", text = $"<mspace=7>[{msg.Timestamp:HH:mm:ss.fff}]</mspace>" };
            timeLabel.AddToClassList("log-item-time");
            header.Add(timeLabel);
            header.Add(messageLabel);

            var callStackLabel = new Label { name = "callStackLabel", text = msg.CallStack };
            callStackLabel.AddToClassList("log-item-callstack");
            callStackLabel.style.display = (msg.Expanded && !string.IsNullOrEmpty(msg.CallStack)) ? DisplayStyle.Flex : DisplayStyle.None;

            element.Add(header);
            element.Add(callStackLabel);

            messageLabel.RegisterCallback<ClickEvent>(evt =>
            {
                msg.Expanded = !msg.Expanded;
                UpdateExpandedState();
                callStackLabel.style.display = (msg.Expanded && !string.IsNullOrEmpty(msg.CallStack)) ? DisplayStyle.Flex : DisplayStyle.None;
            });

            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!element.ClassListContains("hover-active"))
                {
                    ClearHovers();
                    element.AddToClassList("hover-active");
                }
            });
            element.RegisterCallback<PointerLeaveEvent>(evt => element.RemoveFromClassList("hover-active"));

            return element;
        }

        public void Initialize(DevSuiteContext context)
        {
            Reset();
            _context = context;

            _context.OnLogMessagesChanged += HandleLogMessagesChanged;
            _context.OnLogMessagesMessageAdded += HandleLogMessagesMessageAdded;
            _context.OnLogMessagesVisibilityChanged += HandleLogMessagesVisibilityChanged;
            _context.OnFocusCliRequested += FocusCliInput;

            _copyButton.text = "\uf0c5";
            _saveButton.text = "\uf0c7";
            _folderButton.text = "\uf07c";
            _clearButton.text = "\uf2ed";

            _filterField.SetValueWithoutNotify(_context.LogsPattern);
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
                return;

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
            _scrollView.Clear();
            _allMessageElements.Clear();
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

        private void HandleCliSend()
        {
            if (_context == null || _cliInputField == null)
                return;

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
                return false;

            var history = _context.GetCliCommandHistory();
            if (history == null || history.Count == 0)
                return false;

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
                return;

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
                return;

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
                    .ThenBy(x => x.CliCommand, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(x => x.Priority)
                    .ToList();
            }
            else
            {
                var smartRegex = DevSuiteUtils.GetSmartSearchRegex(cmdQueryStr);
                matches = allCommands
                    .Select(c =>
                    {
                        int rank = int.MaxValue;
                        if (ghostCmd != null && c == ghostCmd)
                            rank = -1;
                        else if (string.Equals(c.CliCommand, cmdQueryStr, StringComparison.OrdinalIgnoreCase))
                            rank = 0;
                        else if (c.CliCommand.StartsWith(cmdQueryStr, StringComparison.OrdinalIgnoreCase))
                            rank = 1;
                        else if (c.CliCommand.IndexOf(cmdQueryStr, StringComparison.OrdinalIgnoreCase) >= 0)
                            rank = 2;
                        else if (smartRegex.IsMatch(c.CliCommand))
                            rank = 3;
                        else
                        {
                            var fullPath = $"{c.CategoryName}/{c.GroupName}/{c.CommandId}/{c.CliCommand}";
                            if (fullPath.IndexOf(cmdQueryStr, StringComparison.OrdinalIgnoreCase) >= 0 || smartRegex.IsMatch(fullPath))
                                rank = 4;
                            else if (!string.IsNullOrEmpty(c.Description) && c.Description.IndexOf(cmdQueryStr, StringComparison.OrdinalIgnoreCase) >= 0)
                                rank = 5;
                        }
                        return (cmd: c, rank: rank);
                    })
                    .Where(x => x.rank < int.MaxValue)
                    .OrderBy(x => x.rank)
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

            var pathPrefix = $"{cmd.CategoryName}/{cmd.GroupName}/{cmd.CommandId}/";
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

            item.RegisterCallback<PointerDownEvent>(evt =>
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

                    schedule.Execute(() =>
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
                    });
                }
            });

            return item;
        }

        private void UpdateCliGhost(string currentText)
        {
            if (_cliGhostField == null)
                return;

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
                var valStr = val != null ? (val is string s ? $"\"{s}\"" : val.ToString()) : null;

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
        }

        private void HandleLogMessagesMessageAdded(LogMessageData message)
        {
            if (message.Level == GeneralizedLogSeverity.Ordinary)
                _ordinaryCount++;
            else if (message.Level == GeneralizedLogSeverity.Warning)
                _warningCount++;
            else if (message.Level == GeneralizedLogSeverity.Error)
                _errorCount++;

            UpdateSeverityButtons();

            // Capture scroll state before adding the new element
            var scroller = _scrollView.verticalScroller;
            var wasAtBottom = scroller.highValue <= 0 || scroller.value >= scroller.highValue - 1f;

            var element = CreateLogItem(message);
            _allMessageElements.Add(element);
            _scrollView.Add(element);
            UpdateItemVisibility(element, message);

            // Only auto-scroll if the user was already at the bottom
            //if (wasAtBottom)
            //{
            //    _scrollView.verticalScroller.value = _scrollView.verticalScroller.highValue;
            //}
        }

        private void HandleTextChanged(string newText)
        {
            if (_context == null)
                return;

            _context.LogsPattern = newText;
        }

        private void HandleRegexPressed()
        {
            if (_context == null)
                return;

            _context.LogsRegex = !_context.LogsRegex;
        }

        private void HandleSeverityClick(GeneralizedLogSeverity severity)
        {
            if (_context == null)
                return;

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
                return;

            _context.ClearLogs();
            _filterField.value = "";
            HandleTextChanged("");
        }

        private void HandleCopyPressed()
        {
            DevSuiteUtils.CopyToClipboard(_context.GetAllLogsText());
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
                Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        private void UpdateView()
        {
            if (_context == null)
                return;

            var allMessages = _context.AllLogMessages;
            _allMessageElements.Clear();
            _scrollView.Clear();
            _ordinaryCount = 0;
            _warningCount = 0;
            _errorCount = 0;

            foreach (var m in allMessages)
            {
                if (m.Level == GeneralizedLogSeverity.Ordinary)
                    _ordinaryCount++;
                else if (m.Level == GeneralizedLogSeverity.Warning)
                    _warningCount++;
                else if (m.Level == GeneralizedLogSeverity.Error)
                    _errorCount++;

                var element = CreateLogItem(m);
                _allMessageElements.Add(element);
                _scrollView.Add(element);
            }

            UpdateSeverityButtons();
            UpdateVisibility();

            // Scroll to end only if content overflows the viewport
            //    _scrollView.verticalScroller.value = _scrollView.verticalScroller.highValue;
        }

        private void UpdateVisibility()
        {
            for (var i = 0; i < _allMessageElements.Count; i++)
            {
                var element = _allMessageElements[i];
                var msg = (LogMessageData)element.userData;
                UpdateItemVisibility(element, msg);
                element.EnableInClassList("last-item", i == _allMessageElements.Count - 1);
            }
        }

        private void UpdateItemVisibility(VisualElement element, LogMessageData msg)
        {
            var filterRegex = _context.LogsFilterRegex;
            var matchesFilter = ((filterRegex?.IsMatch(msg.Message) ?? true) || (msg.CallStack != null && (filterRegex?.IsMatch(msg.CallStack) ?? true)));
            var notHidden = !_context.HiddenLogSeverity.Contains(msg.Level);

            element.style.display = (matchesFilter && notHidden) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}