using System;
using System.Collections.Generic;
using System.IO;
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

        private readonly Button _copyButton;
        private readonly Button _saveButton;
        private readonly Button _folderButton;
        private readonly Button _clearButton;

        private readonly ScrollView _scrollView;
        private int _ordinaryCount, _warningCount, _errorCount;

        private readonly HashSet<GeneralizedLogSeverity> _collectStackTraceFor = new() { GeneralizedLogSeverity.Warning, GeneralizedLogSeverity.Error };

        private const string SaveFolderPath = "logger_panel";

        public LogsPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            AddToClassList("ff-panel");

            var root = this.Q<VisualElement>("logs-panel-root") ?? this;

            _filterField = root.Q<TextField>("filterField");
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

            _warningButton = root.Q<Button>("warningButton");
            _warningButton.clicked += () => HandleSeverityClick(GeneralizedLogSeverity.Warning);

            _errorButton = root.Q<Button>("errorButton");
            _errorButton.clicked += () => HandleSeverityClick(GeneralizedLogSeverity.Error);

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

            _copyButton.text = "\uf0c5";
            _saveButton.text = "\uf0c7";
            _folderButton.text = "\uf07c";
            _clearButton.text = "\uf2ed";

            _filterField.SetValueWithoutNotify(_context.LogsPattern);
            UpdateView();
        }

        private void UpdateSeverityButtons()
        {
            _ordinaryButton.text = $"\uf4ad {_ordinaryCount}";
            _warningButton.text = $"\uf071 {_warningCount}";
            _errorButton.text = $"\uf06a {_errorCount}";

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
                _context = null;
            }
            _scrollView.Clear();
            _allMessageElements.Clear();
            _ordinaryCount = 0;
            _warningCount = 0;
            _errorCount = 0;
            UpdateSeverityButtons();
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