using System;
using Ff.DevSuite.Commands;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class CommandsPanelView : VisualElement
    {
        private readonly VisualElement _categoriesContainer;
        private readonly VisualElement _commandsContent;
        private readonly Label _tooltip;

        private Vector2 _lastMousePosition;
        private IVisualElementScheduledItem _tooltipTask;

        private DevSuiteContext _context;
        private ViewMode _mode;
        private string _selectedCategoryId;
        private readonly List<Action> _valueUpdaters = new();

        public CommandsPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);
            AddToClassList("ff-panel");

            RegisterCallback<AttachToPanelEvent>(
                evt =>
                {
                    if (panel != null && !panel.visualTree.styleSheets.Contains(uss))
                    {
                        panel.visualTree.styleSheets.Add(uss);
                    }
                }
            );

            _categoriesContainer = this.Q<VisualElement>("categories-container");
            _commandsContent = this.Q<VisualElement>("commands-content");

            var scrollView = this.Q<ScrollView>("commands-scroll-view");
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            _tooltip = new Label();
            _tooltip.AddToClassList("ff-tooltip");
            _tooltip.pickingMode = PickingMode.Ignore; // Don't block mouse events
            Add(_tooltip);
        }

        private void RegisterTooltip(VisualElement element, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            element.RegisterCallback<MouseEnterEvent>(evt =>
            {
                _lastMousePosition = evt.mousePosition;
                _tooltipTask?.Pause();
                _tooltipTask = schedule.Execute(() =>
                {
                    _tooltip.text = text;
                    _tooltip.style.display = DisplayStyle.Flex;
                    _tooltip.BringToFront();
                    UpdateTooltipPosition(_lastMousePosition);
                }).StartingIn(400);
            });

            element.RegisterCallback<MouseMoveEvent>(evt =>
            {
                _lastMousePosition = evt.mousePosition;
                UpdateTooltipPosition(_lastMousePosition);
            });

            element.RegisterCallback<MouseLeaveEvent>(evt => HideTooltip());
        }

        private void UpdateTooltipPosition(Vector2 position)
        {
            if (_tooltip.style.display == DisplayStyle.None)
            {
                return;
            }

            var localPos = this.WorldToLocal(position);
            _tooltip.style.left = localPos.x + 12;
            _tooltip.style.top = localPos.y + 12;
        }

        private void HideTooltip()
        {
            _tooltipTask?.Pause();
            _tooltip.style.display = DisplayStyle.None;
        }

        internal void Initialize(DevSuiteContext context, ViewMode mode)
        {
            if (_context == context && _mode == mode)
            {
                return;
            }

            Reset();

            _context = context;
            _mode = mode;

            if (_context != null)
            {
                _context.OnChanged += UpdateView;
                _context.OnEveryFrame += UpdateValues;
            }

            UpdateView();
        }

        private void UpdateValues()
        {
            if (!this.IsVisible())
            {
                return;
            }

            foreach (var updater in _valueUpdaters)
            {
                updater.Invoke();
            }
        }

        private void SelectCategory(string categoryId)
        {
            if (_selectedCategoryId == categoryId)
            {
                return;
            }

            _selectedCategoryId = categoryId;
            _context.SelectedCategory = categoryId;
        }

        private void UpdateView()
        {
            if (_context == null || _context.Disposed)
            {
                Reset();
                return;
            }

            if (!this.IsVisible())
                return;

            while (_valueUpdaters.Count > 1)
            {
                _valueUpdaters.RemoveAt(_valueUpdaters.Count - 1);
            }
            while (_categoriesContainer.childCount > 1)
            {
                _categoriesContainer.RemoveAt(_categoriesContainer.childCount - 1);
            }
            _commandsContent.Clear();

            _categoriesContainer.style.display = _mode == ViewMode.Pinned ? DisplayStyle.None : DisplayStyle.Flex;

            if (_mode == ViewMode.Full)
            {
                if (_context?.Tree == null)
                {
                    return;
                }

                if (_context.Tree.Count == 0 && string.IsNullOrEmpty(_context.FilterPattern))
                {
                    return;
                }

                _selectedCategoryId ??= _context.SelectedCategory ?? (_context.Tree.Count > 0 ? _context.Tree[0].Category.Id : null);

                if (_categoriesContainer.childCount <= 0)
                {
                    bool filterFocused = false;

                    var filterPanel = new VisualElement();
                    filterPanel.AddToClassList("ff-commands-filter-panel");

                    var filterIcon = new Label("\uf0b0");
                    filterIcon.AddToClassList("ff-commands-filter-icon");
                    filterPanel.Add(filterIcon);

                    var filterInput = new TextField();
                    filterInput.AddToClassList("ff-commands-filter-input");
                    DevSuiteUtils.SetupInputFieldFocus(filterInput);
                    Button clearFilterButton = null;
                    Action<string> updateClearFilterVisibility = value =>
                    {
                        if (clearFilterButton != null)
                        {
                            clearFilterButton.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
                        }
                    };
                    filterInput.RegisterValueChangedCallback(evt =>
                    {
                        if (_context != null)
                        {
                            _context.FilterPattern = evt.newValue;
                        }
                        updateClearFilterVisibility(evt.newValue);
                    });

                    filterInput.RegisterCallback<FocusInEvent>(_ => filterFocused = true);
                    filterInput.RegisterCallback<FocusOutEvent>(_ => filterFocused = false);

                    clearFilterButton = new Button(() =>
                    {
                        if (_context != null)
                        {
                            _context.FilterPattern = "";
                        }
                        filterInput?.SetValueWithoutNotify("");
                        updateClearFilterVisibility("");
                    })
                    {
                        text = "\uf00d",
                    };
                    clearFilterButton.AddToClassList("ff-commands-filter-clear-button");
                    updateClearFilterVisibility(filterInput.value);

                    filterPanel.Add(filterInput);
                    filterPanel.Add(clearFilterButton);
                    _categoriesContainer.Add(filterPanel);

                    _valueUpdaters.Add(() =>
                    {
                        if (filterInput.style.display == DisplayStyle.None)
                            return;

                        var currentVal = _context?.FilterPattern ?? "";
                        if (!filterFocused && filterInput.value != currentVal)
                        {
                            filterInput.SetValueWithoutNotify(currentVal);
                        }

                        var visibleFilterValue = filterFocused ? filterInput.value : currentVal;
                        updateClearFilterVisibility(visibleFilterValue);
                    });
                }

                foreach (var treeCategory in _context.Tree)
                {
                    var category = treeCategory.Category;

                    var btn = new Button();
                    btn.AddToClassList("ff-commands-category-button");

                    _valueUpdaters.Add(() =>
                    {
                        btn.style.display = _context.CheckVisibilityByVisibilityFunction(category, null) ? DisplayStyle.Flex : DisplayStyle.None;
                    });

                    var label = new Label(category.DisplayName);
                    label.pickingMode = PickingMode.Ignore;
                    if (category.Color != null)
                    {
                        label.style.color = category.Color.Value;
                    }
                    if (category.Id == DevSuiteContext.PinnedCategoryId || category.Id == DevSuiteContext.DefaultGroupId)
                    {
                        label.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
                    }
                    btn.Add(label);

                    if (category.Id == _selectedCategoryId)
                    {
                        btn.AddToClassList("ff-commands-category-button--selected");
                    }

                    btn.clicked += () =>
                    {
                        SelectCategory(category.Id);
                    };

                    RegisterTooltip(btn, category.Description);
                    _categoriesContainer.Add(btn);
                }
            }

            UpdateCommandsContent();
            UpdateValues();
        }

        private List<TreeCategory> _pinnedListCached;
        private void UpdateCommandsContent()
        {
            var tree = _context?.Tree;
            if (_mode == ViewMode.Pinned)
            {
                tree = _pinnedListCached ??= _context.GetPinnedList();
                _selectedCategoryId = DevSuiteContext.PinnedMockId;
                var group = tree[0].Groups[0].Commands[0];
                group.ChangeCommands(new List<Command>(_context.GetPinnedCommands(false)));
                tree[0].Groups[0].Commands.AsEditable()[0] = group;
            }
            if (tree == null)
            {
                return;
            }

            TreeCategory treeItem = default;
            foreach (var ti in tree)
            {
                if (ti.Category.Id == _selectedCategoryId)
                {
                    treeItem = ti;
                }
            }
            if (treeItem.Groups == null)
            {
                return;
            }

            CommandsPanelGroupView lastGroupView = null;
            foreach (var groupData in treeItem.Groups)
            {
                var groupName = groupData.Group.DisplayName;
                foreach (var cmd in groupData.Commands)
                {
                    var (targetInstance, commands) = (cmd.TargetInstance, cmd.Commands);
                    var groupNameWithInstanceId = groupName;
                    if (targetInstance != null)
                    {
                        groupNameWithInstanceId = $"{groupName} <color=#777777>({targetInstance.GetType().Name}#{targetInstance.GetHashCode()})</color>";
                    }

                    var groupView = new CommandsPanelGroupView(
                        _context,
                        groupData.Group.CategoryId,
                        groupData.Group.Id,
                        groupNameWithInstanceId,
                        _mode,
                        groupData.Group.Description,
                        groupData.Group.Color,
                        groupData.Group.Collapsed
                    );
                    _commandsContent.Add(groupView);
                    lastGroupView = groupView;

                    _valueUpdaters.Add(() =>
                    {
                        groupView.style.display = _context.CheckVisibilityByVisibilityFunction(groupData.Group, null) ? DisplayStyle.Flex : DisplayStyle.None;
                    });

                    VisualElement lastCommandView = null;
                    foreach (var command in commands)
                    {
                        var commandView = new CommandsPanelCommandView(command, _context, RegisterTooltip);
                        groupView.Content.Add(commandView);
                        lastCommandView = commandView;

                        _valueUpdaters.Add(() =>
                        {
                            commandView.style.display = _context.CheckVisibilityByVisibilityFunction(command, null) ? DisplayStyle.Flex : DisplayStyle.None;
                        });

                        VisualElement lastUnitView = null;
                        foreach (var unit in command.Units)
                        {
                            VisualElement unitView = null;

                            _valueUpdaters.Add(() =>
                            {
                                if (unitView == null)
                                    return;
                                unitView.style.display = _context.CheckUnitAvailability(unit) ? DisplayStyle.Flex : DisplayStyle.None;
                            });

                            unitView = CreateUnitView(unit, command);
                            if (unitView != null)
                            {
                                RegisterTooltip(unitView, unit.Description);
                                commandView.UnitsContainer.Add(unitView);
                                lastUnitView = unitView;
                            }
                        }

                        if (lastUnitView != null)
                        {
                            lastUnitView.AddToClassList("ff-commands-unit--last");
                        }
                    }

                    if (lastCommandView != null)
                    {
                        lastCommandView.AddToClassList("ff-commands-command-row--last");
                    }
                }
            }

            if (lastGroupView != null)
            {
                lastGroupView.AddToClassList("ff-commands-group--last");
            }
        }

        private VisualElement CreateUnitView(BaseCommandUnit unit, Command command)
        {
            VisualElement element = null;

            if (unit is CommandUnitButton btnUnit)
            {
                var text = btnUnit.Text ?? btnUnit.Description ?? "Action";
                if (btnUnit.Shortcut != null && btnUnit.Shortcut.Length > 0)
                {
                    var shortcutStr = string.Join("+", btnUnit.Shortcut);
                    text = $"{text} <color=#cc7474>({shortcutStr})</color>";
                }

                var btn = new Button
                {
                    text = text,
                };
                btn.AddToClassList("ff-commands-unit-button");
                btn.clicked += () => _context.ExecuteButton(btnUnit);
                element = btn;
            }
            else if (unit is CommandUnitValue unitValue)
            {
                var hasLimitedValues = _context.HasLimitedValues(unitValue);

                var underlyingInfo = _context.GetUnderlyingPrimitiveType(unitValue, true);
                var underlyingType = underlyingInfo?.Type ?? unitValue.Type;
                var isNullable = underlyingInfo?.IsNullable ?? _context.HasUnderlyingNullableType(unitValue);

                Toggle hasValueToggle = null;
                Action updateNullState = null;

                var canSetBack = true;
                if (underlyingType == typeof(bool))
                {
                    canSetBack = _context.CanConvert(typeof(bool), unitValue.Type);

                    var toggle = new Toggle();
                    toggle.AddToClassList("ff-commands-unit-toggle");

                    var checkmark = toggle.Q<VisualElement>("unity-checkmark");
                    if (checkmark != null)
                    {
                        var icon = new Label("\uf00c");
                        icon.AddToClassList("ff-commands-unit-toggle-icon");
                        checkmark.Add(icon);
                    }

                    toggle.RegisterValueChangedCallback(
                        evt =>
                        {
                            if (isNullable && hasValueToggle != null && !hasValueToggle.value)
                            {
                                return;
                            }
                            _context.SetByRepresentation(unitValue, evt.newValue, out _);
                        }
                    );
                    element = toggle;
                    _valueUpdaters.Add(() =>
                    {
                        if (element.style.display == DisplayStyle.None)
                            return;

                        var val = _context.GetRepresentation<bool?>(unitValue, out _);
                        if (toggle.value != (val ?? false))
                        {
                            toggle.SetValueWithoutNotify(val ?? false);
                        }
                    });

                    updateNullState = () =>
                    {
                        if (hasValueToggle != null && !hasValueToggle.value)
                        {
                            _context.SetByRepresentation(unitValue, null, out _);
                        }
                        else
                        {
                            _context.SetByRepresentation(unitValue, toggle.value, out _);
                        }
                    };
                }
                else if (!unitValue.ForceStringRepresentation && hasLimitedValues)
                {
                    canSetBack = _context.CanConvert(typeof(string), unitValue.Type);

                    var dropdown = new DropdownField();
                    dropdown.AddToClassList("ff-commands-unit-dropdown");

                    List<object> items = null;

                    dropdown.RegisterValueChangedCallback(
                        evt =>
                        {
                            if (isNullable && hasValueToggle is { value: false })
                            {
                                return;
                            }

                            var idx = dropdown.index;
                            if (idx >= 0 && idx < items.Count)
                            {
                                _context.SetByRepresentation(unitValue, items[idx], out _);
                            }
                        }
                    );

                    element = dropdown;

                    _valueUpdaters.Add(() =>
                    {
                        if (element.style.display == DisplayStyle.None)
                            return;

                        if (_context.GetAllowedValues(unitValue) is { } currentAv)
                        {
                            var data = currentAv;

                            items = new List<object>();
                            var choices = new List<string>();
                            foreach (var item in data.Values)
                            {
                                items.Add(item);
                                var name = _context.GetRepresentation<string>(item, item?.GetType() ?? data.Type, out var error, true, unitValue.SuppressExceptions) ?? DevSuiteContext.NullRepresentation;
                                name = string.IsNullOrEmpty(error) ? name : error;
                                choices.Add(name);
                            }

                            dropdown.choices = choices;

                            var currentSavedValue = _context.GetRepresentation<string>(unitValue, out _);

                            var i = 0;
                            for (; i < items.Count; i++)
                            {
                                var item = items[i];
                                var currentPossibleValue = _context.GetRepresentation<string>(item, item?.GetType() ?? typeof(string), out _);
                                if (currentPossibleValue == currentSavedValue)
                                {
                                    dropdown.SetValueWithoutNotify(dropdown.choices[i]);
                                    return;
                                }
                            }
                        }
                        dropdown.SetValueWithoutNotify(null);
                    });

                    updateNullState = () =>
                    {
                        if (hasValueToggle is { value: false })
                        {
                            _context.SetByRepresentation(unitValue, null, out _);
                        }
                        else
                        {
                            var idx = -1;
                            while (++idx < items.Count)
                            {
                                if (items[idx] != null)
                                {
                                    _context.SetByRepresentation(unitValue, items[idx], out _);
                                    return;
                                }
                            }
                            _context.SetByRepresentation(unitValue, null, out _);
                        }
                    };
                }
                else if (!unitValue.ForceStringRepresentation && unitValue.ValuesRange != null)
                {
                    canSetBack = _context.CanConvert(typeof(float), unitValue.Type);

                    var slider = new Slider(0f, 1f);
                    slider.AddToClassList("ff-commands-unit-slider");
                    slider.RegisterValueChangedCallback(
                        evt =>
                        {
                            if (isNullable && hasValueToggle != null && !hasValueToggle.value)
                            {
                                return;
                            }
                            var actualValue = unitValue.FromSliderToValue(evt.newValue);
                            _context.SetByRepresentation(unitValue, actualValue, out _);
                        }
                    );
                    element = slider;

                    var sliderHasFocus = false;
                    slider.RegisterCallback<FocusInEvent>(_ => sliderHasFocus = true);
                    slider.RegisterCallback<FocusOutEvent>(_ => sliderHasFocus = false);

                    _valueUpdaters.Add(() =>
                    {
                        if (element.style.display == DisplayStyle.None)
                            return;

                        if (sliderHasFocus)
                        {
                            return;
                        }

                        var currentStr = _context.GetRepresentation<string>(unitValue, out _, true);
                        var currentVal = Convert.ToSingle(currentStr ?? "0");
                        var sliderValue = unitValue.FromValueToSlider(currentVal);
                        if (Mathf.Abs(slider.value - sliderValue) > 0.001f)
                        {
                            slider.SetValueWithoutNotify(sliderValue);
                        }
                    });

                    updateNullState = () =>
                    {
                        if (hasValueToggle != null && !hasValueToggle.value)
                        {
                            _context.SetByRepresentation(unitValue, null, out _);
                        }
                        else
                        {
                            var actualValue = unitValue.FromSliderToValue(slider.value);
                            _context.SetByRepresentation(unitValue, actualValue, out _);
                        }
                    };
                }
                else
                {
                    canSetBack = _context.CanConvert(typeof(string), unitValue.Type);

                    // Default TextField representation
                    var field = new TextField();
                    field.AddToClassList("ff-commands-unit-text");
                    DevSuiteUtils.SetupInputFieldFocus(field);
                    if (command.HeightMultiplier > 1)
                    {
                        field.multiline = true;
                    }

                    var isInteger = underlyingType.IsInteger();
                    var isNumber = underlyingType.IsNumber();

                    if (isNumber && !unitValue.ReadOnly)
                    {
                        field.RegisterCallback<InputEvent>(evt =>
                        {
                            var filtered = FilterNumericInput(evt.newData, isInteger);
                            if (filtered != evt.newData)
                            {
                                field.value = filtered;
                            }
                        });
                    }

                    if (!unitValue.ReadOnly)
                    {
                        field.RegisterValueChangedCallback(
                            evt =>
                            {
                                if (isNullable && hasValueToggle != null && !hasValueToggle.value)
                                {
                                    return;
                                }

                                _context.SetByRepresentation(unitValue, evt.newValue, out _);
                            }
                        );
                    }
                    element = field;

                    var fieldHasFocus = false;
                    field.RegisterCallback<FocusInEvent>(_ => fieldHasFocus = true);
                    field.RegisterCallback<FocusOutEvent>(_ => fieldHasFocus = false);

                    _valueUpdaters.Add(() =>
                    {
                        if (element.style.display == DisplayStyle.None)
                            return;

                        if (fieldHasFocus)
                        {
                            return;
                        }
                        var currentStr = _context.GetRepresentation<string>(unitValue, out _, true);
                        if (field.value != (currentStr ?? ""))
                        {
                            field.SetValueWithoutNotify(currentStr ?? "");
                        }
                    });

                    updateNullState = () =>
                    {
                        if (hasValueToggle != null && !hasValueToggle.value)
                        {
                            _context.SetByRepresentation(unitValue, null, out _);
                        }
                        else
                        {
                            _context.SetByRepresentation(unitValue, field.value, out _);
                        }
                    };
                }

                if (isNullable)
                {
                    var container = new VisualElement();
                    container.style.flexDirection = FlexDirection.Row;
                    container.style.alignItems = Align.Center;
                    container.AddToClassList("ff-commands-unit-nullable-container");

                    hasValueToggle = new Toggle();
                    hasValueToggle.AddToClassList("ff-commands-unit-toggle");
                    hasValueToggle.AddToClassList("ff-commands-unit-nullable-toggle");

                    var checkmark = hasValueToggle.Q<VisualElement>("unity-checkmark");
                    if (checkmark != null)
                    {
                        var icon = new Label("\uf00c");
                        icon.AddToClassList("ff-commands-unit-toggle-icon");
                        checkmark.Add(icon);
                    }

                    var control = element;
                    if (unitValue.ReadOnly)
                    {
                        hasValueToggle.SetEnabled(false);
                        SetElementEnabled(control, false);
                    }
                    else
                    {
                        hasValueToggle.RegisterValueChangedCallback(
                            evt =>
                            {
                                updateNullState?.Invoke();
                            }
                        );
                    }

                    _valueUpdaters.Add(() =>
                    {
                        if (element.style.display == DisplayStyle.None)
                            return;

                        var currentStr = _context.GetRepresentation<string>(unitValue, out _, true);
                        var currentHasValue = currentStr != null && currentStr != DevSuiteContext.NullRepresentation;
                        if (hasValueToggle.value != currentHasValue)
                        {
                            hasValueToggle.SetValueWithoutNotify(currentHasValue);
                        }
                        if (!unitValue.ReadOnly && GetElementEnabled(control) != currentHasValue)
                        {
                            SetElementEnabled(control, currentHasValue);
                        }
                    });

                    container.Add(hasValueToggle);

                    if (element is Slider || element is TextField || element is DropdownField)
                    {
                        container.style.flexGrow = 1;
                        element.style.flexGrow = 1;
                    }

                    element.style.marginLeft = 0;
                    element.style.marginRight = 0;
                    element.style.marginTop = 0;
                    element.style.marginBottom = 0;
                    element.style.paddingLeft = 0;
                    element.style.paddingRight = 0;
                    element.style.paddingTop = 0;
                    element.style.paddingBottom = 0;

                    container.Add(element);
                    element = container;
                }

                if (unitValue.ReadOnly || !canSetBack)
                {
                    SetElementEnabled(element, false);
                }
            }

            if (element != null)
            {
                element.AddToClassList("ff-commands-unit");
                if (element is Toggle)
                {
                    element.style.flexGrow = 0;
                    element.style.flexShrink = 0;
                }
                else if (unit.Flex >= 0)
                {
                    element.style.flexGrow = unit.Flex;
                    if (unit.Flex > 0)
                    {
                        element.style.flexBasis = 0;
                    }
                }

                if (unit.Color != null)
                {
                    element.style.color = unit.Color.Value;
                }

                if (!string.IsNullOrEmpty(unit.FontResource))
                {
                    ApplyFontResource(element, unit.FontResource);
                }

                if (command.HeightMultiplier > 1)
                {
                    if (element.ClassListContains("ff-commands-unit-text") || element is TextField)
                    {
                        element.style.height = StyleKeyword.Auto;
                    }
                }
            }

            return element;
        }

        private static void ApplyFontResource(VisualElement element, string fontResource)
        {
            var fontAsset = Resources.Load<FontAsset>(fontResource);
            if (fontAsset != null)
            {
                element.style.unityFontDefinition = FontDefinition.FromSDFFont(fontAsset);
                return;
            }

            var font = Resources.Load<Font>(fontResource);
            if (font != null)
            {
                element.style.unityFontDefinition = FontDefinition.FromFont(font);
            }
        }

        private void SetElementEnabled(VisualElement element, bool enabled)
        {
            if (element is TextField textField)
            {
                textField.isReadOnly = !enabled;
                if (enabled)
                {
                    textField.RemoveFromClassList("ff-commands-unit-text--disabled");
                    textField.SetEnabled(true);
                }
                else
                {
                    textField.AddToClassList("ff-commands-unit-text--disabled");
                    textField.SetEnabled(true);
                    // We don't call SetEnabled(false) here to keep the text selectable for copying
                }
            }
            else if (element.ClassListContains("ff-commands-unit-nullable-container"))
            {
                foreach (var child in element.Children())
                {
                    SetElementEnabled(child, enabled);
                }
            }
            else
            {
                element.SetEnabled(enabled);
            }
        }

        private bool GetElementEnabled(VisualElement element)
        {
            if (element is TextField textField)
            {
                return !textField.isReadOnly;
            }

            if (element.ClassListContains("ff-commands-unit-nullable-container"))
            {
                foreach (var child in element.Children())
                {
                    return GetElementEnabled(child);
                }
            }

            return element.enabledSelf;
        }

        private string FilterNumericInput(string input, bool isInteger)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var sb = new System.Text.StringBuilder();
            var hasDecimal = false;
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (i == 0 && c == '-')
                {
                    sb.Append(c);
                }
                else if (char.IsDigit(c))
                {
                    sb.Append(c);
                }
                else if (!isInteger && (c == '.' || c == ',') && !hasDecimal)
                {
                    sb.Append(c);
                    hasDecimal = true;
                }
            }

            return sb.ToString();
        }

        public void Reset()
        {
            _valueUpdaters.Clear();
            if (_context != null)
            {
                _context.OnChanged -= UpdateView;
                _context.OnEveryFrame -= UpdateValues;
                _context = null;
            }
            _categoriesContainer?.Clear();
            _commandsContent?.Clear();
        }

        internal enum ViewMode
        {
            Full,
            Pinned,
        }
    }
}