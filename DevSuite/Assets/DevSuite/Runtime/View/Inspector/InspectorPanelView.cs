using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Ff.DevSuite;

namespace Ff.DevSuite.View
{
    internal class InspectorPanelView : VisualElement
    {
        private DevSuiteContext _context;
        private readonly Label _selectedObjectLabel;
        private readonly Label _selectedObjectPathLabel;
        private readonly ScrollView _scrollView;
        private readonly Button _copyBtn;
        private readonly Toggle _goActivityToggle;
        private EventCallback<ChangeEvent<bool>> _goActivityCallback;

        private struct TrackedProperty
        {
            public object Target;
            public MemberInfo Member;
            public Label ValueLabel;
        }

        private readonly List<TrackedProperty> _trackedProperties = new();
        private readonly List<GameObject> _lastSelectedGameObjects = new();

        private struct TrackedGoActivity
        {
            public GameObject Go;
            public Toggle ToggleControl;
            public VisualElement NameContainer; // container to apply 'inactive' dimming class on
        }

        private readonly List<TrackedGoActivity> _trackedGoActivities = new();

        private struct TrackedMonoBehaviour
        {
            public MonoBehaviour Mono;
            public Toggle ToggleControl;
            public VisualElement CompBox;
        }

        private readonly List<TrackedMonoBehaviour> _trackedMonoBehaviours = new();

        private struct CachedTypeData
        {
            public List<FieldInfo> SerializableFields;
            public List<MemberInfo> OtherMembers;
        }

        private static readonly Dictionary<Type, CachedTypeData> _typeCache = new();

        private CachedTypeData GetOrCreateCachedTypeData(Type type)
        {
            if (_typeCache.TryGetValue(type, out var cachedData))
            {
                return cachedData;
            }

            var members = type.GetFieldsAndProperties();

            var serializableFields = new List<FieldInfo>();
            var otherMembers = new List<MemberInfo>();

            foreach (var member in members)
            {
                if (member.Name.StartsWith("<")) continue;
                if (member.GetCustomAttribute<ObsoleteAttribute>() != null) continue;

                if (member is FieldInfo field)
                {
                    if (IsSerializable(field))
                    {
                        serializableFields.Add(field);
                    }
                    else
                    {
                        otherMembers.Add(field);
                    }
                }
                else if (member is PropertyInfo prop)
                {
                    if (prop.DeclaringType == typeof(Component) ||
                        prop.DeclaringType == typeof(MonoBehaviour) ||
                        prop.DeclaringType == typeof(Behaviour) ||
                        prop.DeclaringType == typeof(UnityEngine.Object))
                    {
                        continue;
                    }

                    otherMembers.Add(prop);
                }
            }

            cachedData = new CachedTypeData
            {
                SerializableFields = serializableFields,
                OtherMembers = otherMembers
            };

            _typeCache[type] = cachedData;
            return cachedData;
        }

        public InspectorPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            AddToClassList("ff-panel");

            var root = this.Q<VisualElement>("inspector-panel-root") ?? this;

            _selectedObjectLabel = root.Q<Label>("selectedObjectLabel");
            _selectedObjectPathLabel = root.Q<Label>("selectedObjectPathLabel");
            _scrollView = root.Q<ScrollView>("inspectorScrollView");
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            // Header activity toggle — wired dynamically in RebuildInspector since it needs the current GO reference
            _goActivityToggle = root.Q<Toggle>("goActivityToggle");
            if (_goActivityToggle != null)
            {
                var checkmark = _goActivityToggle.Q<VisualElement>("unity-checkmark");
                if (checkmark != null)
                {
                    var icon = new Label("\uf00c");
                    icon.AddToClassList("ff-toggle-icon");
                    checkmark.Add(icon);
                }
            }

            _copyBtn = root.Q<Button>("copyBtn");
            if (_copyBtn != null)
            {
                _copyBtn.text = "\uf0c5"; // copy icon
                _copyBtn.clicked += () =>
                {
                    if (_context != null && _context.SelectedGameObjects.Count > 0)
                    {
                        var inspectorText = GetInspectorText(_context.SelectedGameObjects);
                        DevSuiteUtils.CopyToClipboard(inspectorText);
                        DevSuiteUtils.ShowIconButtonClickedFeedback(_copyBtn);
                    }
                };
            }

            DevSuiteUtils.SetupTooltips(this);
        }

        public void Initialize(DevSuiteContext context)
        {
            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= HandleOnEveryFrame;
            }

            _context = context;

            if (_context != null)
            {
                _context.OnChanged += HandleContextChanged;
                _context.OnEveryFrame += HandleOnEveryFrame;
                UpdateInspector();
            }
        }

        public void Reset()
        {
            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= HandleOnEveryFrame;
                _context = null;
            }
        }

        private void HandleContextChanged()
        {
            UpdateInspector();
        }

        private void UpdateInspector()
        {
            if (_context == null)
            {
                return;
            }

            var currentSelection = _context.SelectedGameObjects;
            var changed = false;
            if (currentSelection.Count != _lastSelectedGameObjects.Count)
            {
                changed = true;
            }
            else
            {
                for (var i = 0; i < currentSelection.Count; i++)
                {
                    if (currentSelection[i] != _lastSelectedGameObjects[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                RebuildInspector(currentSelection);
            }
        }

        private void RebuildInspector(IReadOnlyList<GameObject> gameObjects)
        {
            _lastSelectedGameObjects.Clear();
            if (gameObjects != null)
            {
                _lastSelectedGameObjects.AddRange(gameObjects);
            }
            _scrollView.Clear();
            _trackedProperties.Clear();
            _trackedMonoBehaviours.Clear();
            _trackedGoActivities.Clear();

            // Reset the header toggle — callbacks are re-registered below
            if (_goActivityToggle != null && _goActivityCallback != null)
            {
                _goActivityToggle.UnregisterValueChangedCallback(_goActivityCallback);
                _goActivityCallback = null;
            }

            if (gameObjects == null || gameObjects.Count == 0)
            {
                _selectedObjectLabel.text = "None selected";
                _selectedObjectPathLabel.text = "";
                if (_goActivityToggle != null)
                {
                    _goActivityToggle.style.display = DisplayStyle.None;
                }
                return;
            }

            if (gameObjects.Count == 1)
            {
                var go = gameObjects[0];
                _selectedObjectLabel.text = go.name;
                _selectedObjectPathLabel.text = DevSuiteUtils.GetGameObjectPath(go);

                if (_goActivityToggle != null)
                {
                    _goActivityToggle.style.display = DisplayStyle.Flex;
                    _goActivityToggle.SetValueWithoutNotify(go.activeSelf);
                    if (!go.activeSelf) _selectedObjectLabel.AddToClassList("inactive");
                    else _selectedObjectLabel.RemoveFromClassList("inactive");
                    _goActivityCallback = evt =>
                    {
                        if (go != null)
                        {
                            go.SetActive(evt.newValue);
                            if (evt.newValue) _selectedObjectLabel.RemoveFromClassList("inactive");
                            else _selectedObjectLabel.AddToClassList("inactive");
                        }
                    };
                    _goActivityToggle.RegisterValueChangedCallback(_goActivityCallback);
                    _trackedGoActivities.Add(new TrackedGoActivity { Go = go, ToggleControl = _goActivityToggle, NameContainer = _selectedObjectLabel });
                }

                var components = go.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null)
                    {
                        continue; // missing scripts
                    }

                    RenderComponent(comp, _scrollView);
                }
            }
            else
            {
                _selectedObjectLabel.text = "Multiple Selected";
                _selectedObjectPathLabel.text = $"{gameObjects.Count} objects selected";

                if (_goActivityToggle != null)
                {
                    _goActivityToggle.style.display = DisplayStyle.None;
                }

                foreach (var go in gameObjects)
                {
                    if (go == null)
                    {
                        continue;
                    }

                    var goHeader = new VisualElement();
                    goHeader.AddToClassList("inspector-go-header");

                    // Per-GO activity toggle
                    var goToggle = new Toggle
                    {
                        value = go.activeSelf,
                        tooltip = "Toggle active state"
                    };
                    goToggle.AddToClassList("ff-toggle");
                    goToggle.AddToClassList("inspector-go-activity-toggle");
                    var checkmark = goToggle.Q<VisualElement>("unity-checkmark");
                    if (checkmark != null)
                    {
                        var icon = new Label("\uf00c");
                        icon.AddToClassList("ff-toggle-icon");
                        checkmark.Add(icon);
                    }
                    goToggle.RegisterValueChangedCallback(evt =>
                    {
                        if (go != null)
                        {
                            go.SetActive(evt.newValue);
                            if (evt.newValue) goHeader.RemoveFromClassList("inactive");
                            else goHeader.AddToClassList("inactive");
                        }
                    });
                    var textContainer = new VisualElement();
                    textContainer.AddToClassList("inspector-go-header-text");

                    var goNameLabel = new Label(go.name);
                    goNameLabel.AddToClassList("inspector-go-name");
                    textContainer.Add(goNameLabel);

                    var goPathLabel = new Label(DevSuiteUtils.GetGameObjectPath(go));
                    goPathLabel.AddToClassList("inspector-go-path");
                    textContainer.Add(goPathLabel);

                    goHeader.Add(textContainer);

                    // Toggle added last so margin-left: auto pushes it to the right
                    goHeader.Add(goToggle);
                    if (!go.activeSelf) goHeader.AddToClassList("inactive");
                    _trackedGoActivities.Add(new TrackedGoActivity { Go = go, ToggleControl = goToggle, NameContainer = goHeader });

                    _scrollView.Add(goHeader);

                    var components = go.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null)
                        {
                            continue; // missing scripts
                        }

                        RenderComponent(comp, _scrollView);
                    }
                }
            }
        }

        private void RenderComponent(Component comp, VisualElement container)
        {
            var compType = comp.GetType();

            var compBox = new VisualElement
            {
                name = "compBox",
            };
            compBox.AddToClassList("inspector-component-box");
            container.Add(compBox);

            var header = new VisualElement
            {
                name = "compHeader",
            };
            header.AddToClassList("inspector-component-header");
            compBox.Add(header);

            Toggle enabledToggle = null;
            if (comp is MonoBehaviour mono)
            {
                enabledToggle = new Toggle
                {
                    name = "monoEnabledToggle",
                    value = mono.enabled,
                    tooltip = "Toggle enabled state"
                };
                enabledToggle.AddToClassList("ff-toggle");
                enabledToggle.AddToClassList("inspector-mono-toggle");
                var monoCheckmark = enabledToggle.Q<VisualElement>("unity-checkmark");
                if (monoCheckmark != null)
                {
                    var icon = new Label("\uf00c");
                    icon.AddToClassList("ff-toggle-icon");
                    monoCheckmark.Add(icon);
                }
                if (!mono.enabled)
                {
                    compBox.AddToClassList("disabled");
                }
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    if (mono != null)
                    {
                        mono.enabled = evt.newValue;
                        if (evt.newValue)
                        {
                            compBox.RemoveFromClassList("disabled");
                        }
                        else
                        {
                            compBox.AddToClassList("disabled");
                        }
                    }
                });
                _trackedMonoBehaviours.Add(new TrackedMonoBehaviour { Mono = mono, ToggleControl = enabledToggle, CompBox = compBox });
            }

            var nameLabel = new Label
            {
                name = "compName",
                text = compType.Name,
            };
            nameLabel.AddToClassList("inspector-component-name");
            header.Add(nameLabel);

            var typeLabel = new Label
            {
                name = "compType",
                text = compType.Namespace ?? "",
            };
            typeLabel.AddToClassList("inspector-component-type");
            header.Add(typeLabel);

            if (enabledToggle != null)
            {
                header.Add(enabledToggle);
            }

            var body = new VisualElement
            {
                name = "compBody",
            };
            body.AddToClassList("inspector-component-body");
            compBox.Add(body);

            var cachedData = GetOrCreateCachedTypeData(compType);

            foreach (var field in cachedData.SerializableFields)
            {
                RenderPropertyRow(comp, field, true, body);
            }

            foreach (var member in cachedData.OtherMembers)
            {
                RenderPropertyRow(comp, member, false, body);
            }
        }

        private void RenderPropertyRow(Component comp, MemberInfo member, bool isSerializable, VisualElement container)
        {
            var row = new VisualElement();
            row.AddToClassList("inspector-property-row");
            row.AddToClassList(isSerializable ? "serializable" : "non-serializable");

            var nameLabel = new Label
            {
                name = "propName",
                text = member.Name,
            };
            nameLabel.AddToClassList("inspector-property-name");
            row.Add(nameLabel);

            var valueLabel = new Label
            {
                name = "propValue",
            };
            valueLabel.AddToClassList("inspector-property-value");
            row.Add(valueLabel);

            container.Add(row);

            try
            {
                var initialVal = member switch
                {
                    FieldInfo fi => fi.GetValue(comp),
                    PropertyInfo pi => pi.GetValue(comp),
                    _ => null,
                };
                valueLabel.text = FormatValue(initialVal);
            }
            catch
            {
                valueLabel.text = "<error>";
            }

            _trackedProperties.Add(
                new TrackedProperty
                {
                    Target = comp,
                    Member = member,
                    ValueLabel = valueLabel,
                }
            );
        }

        private bool IsSerializable(FieldInfo field)
        {
            if (field.IsInitOnly)
            {
                return false;
            }
            if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
            {
                return false;
            }
            if (field.IsPublic)
            {
                return true;
            }
            if (field.GetCustomAttribute<SerializeField>() != null)
            {
                return true;
            }
            return false;
        }

        private string FormatValue(object value)
        {
            if (value == null || value.Equals(null))
            {
                return "null";
            }

            if (value is UnityEngine.Object obj)
            {
                return obj == null
                    ? "null"
                    : $"[{obj.GetType().Name}] {obj.name}";
            }

            return value.ToString();
        }

        private void HandleOnEveryFrame()
        {
            if (_context == null || _lastSelectedGameObjects.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _lastSelectedGameObjects.Count; i++)
            {
                var go = _lastSelectedGameObjects[i];
                if (go == null || go.Equals(null))
                {
                    RebuildInspector(null);
                    return;
                }
            }

            foreach (var prop in _trackedProperties)
            {
                if (prop.Target == null || prop.Target.Equals(null))
                {
                    continue;
                }

                try
                {
                    var val = prop.Member switch
                    {
                        FieldInfo fi => fi.GetValue(prop.Target),
                        PropertyInfo pi => pi.GetValue(prop.Target),
                        _ => null,
                    };

                    prop.ValueLabel.text = FormatValue(val);
                }
                catch
                {
                    prop.ValueLabel.text = "<error>";
                }
            }

            for (var i = _trackedMonoBehaviours.Count - 1; i >= 0; i--)
            {
                var tracked = _trackedMonoBehaviours[i];
                if (tracked.Mono == null || tracked.Mono.Equals(null))
                {
                    _trackedMonoBehaviours.RemoveAt(i);
                    continue;
                }

                bool isEnabled = tracked.Mono.enabled;
                if (tracked.ToggleControl.value != isEnabled)
                {
                    tracked.ToggleControl.SetValueWithoutNotify(isEnabled);
                    if (isEnabled)
                    {
                        tracked.CompBox.RemoveFromClassList("disabled");
                    }
                    else
                    {
                        tracked.CompBox.AddToClassList("disabled");
                    }
                }
            }

            for (var i = _trackedGoActivities.Count - 1; i >= 0; i--)
            {
                var tracked = _trackedGoActivities[i];
                if (tracked.Go == null || tracked.Go.Equals(null))
                {
                    _trackedGoActivities.RemoveAt(i);
                    continue;
                }

                bool isActive = tracked.Go.activeSelf;
                if (tracked.ToggleControl.value != isActive)
                {
                    tracked.ToggleControl.SetValueWithoutNotify(isActive);
                    if (isActive) tracked.NameContainer?.RemoveFromClassList("inactive");
                    else tracked.NameContainer?.AddToClassList("inactive");
                }
            }
        }

        private GameObject GetSelectedGameObject()
        {
            if (_context != null && _context.SelectedGameObject != null)
            {
                return _context.SelectedGameObject;
            }
#if UNITY_EDITOR
            if (UnityEditor.Selection.activeGameObject != null)
            {
                return UnityEditor.Selection.activeGameObject;
            }
#endif
            return null;
        }

        private string GetInspectorText(IReadOnlyList<GameObject> gameObjects)
        {
            if (gameObjects == null || gameObjects.Count == 0)
            {
                return "";
            }

            var sb = new System.Text.StringBuilder();
            foreach (var go in gameObjects)
            {
                if (go == null)
                {
                    continue;
                }

                var goDisabledStr = !go.activeSelf ? " (inactive)" : "";
                sb.AppendLine($"GameObject: {go.name}{goDisabledStr}");
                sb.AppendLine($"Path: {DevSuiteUtils.GetGameObjectPath(go)}");
                sb.AppendLine();

                var components = go.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null)
                    {
                        continue;
                    }

                    var compType = comp.GetType();
                    var mono = comp as MonoBehaviour;
                    var compDisabledStr = (mono != null && !mono.enabled) ? " (disabled)" : "";
                    sb.AppendLine($"{compType.Name} ({compType.Namespace ?? "UnityEngine"}){compDisabledStr}");

                    var cachedData = GetOrCreateCachedTypeData(compType);

                    foreach (var field in cachedData.SerializableFields)
                    {
                        AppendPropertyText(comp, field, sb);
                    }

                    foreach (var member in cachedData.OtherMembers)
                    {
                        AppendPropertyText(comp, member, sb);
                    }

                    sb.AppendLine();
                }

                sb.AppendLine(new string('-', 30));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void AppendPropertyText(Component comp, MemberInfo member, System.Text.StringBuilder sb)
        {
            try
            {
                var val = member switch
                {
                    FieldInfo fi => fi.GetValue(comp),
                    PropertyInfo pi => pi.GetValue(comp),
                    _ => null,
                };
                var formattedVal = FormatValue(val);
                if (formattedVal != null && (formattedVal.Contains("\n") || formattedVal.Contains("\r")))
                {
                    sb.AppendLine($"  {member.Name}:");
                    var lines = DevSuiteUtils.NewLineRegex.Split(formattedVal);
                    foreach (var line in lines)
                    {
                        sb.AppendLine($"    {line}");
                    }
                }
                else
                {
                    sb.AppendLine($"  {member.Name}: {formattedVal}");
                }
            }
            catch
            {
                sb.AppendLine($"  {member.Name}: <error>");
            }
        }
    }
}