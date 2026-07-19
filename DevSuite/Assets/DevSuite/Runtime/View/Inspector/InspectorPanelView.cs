using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class InspectorPanelView : VisualElement
    {
        private DevSuiteContext _context;
        private readonly Label _selectedObjectLabel;
        private readonly Label _selectedObjectPathLabel;
        private readonly ScrollView _scrollView;

        private struct TrackedProperty
        {
            public object Target;
            public MemberInfo Member;
            public Label ValueLabel;
        }

        private readonly List<TrackedProperty> _trackedProperties = new();
        private GameObject _lastSelectedGameObject;

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
            DevSuiteUtils.SetupTooltips(this);
        }

        public void Initialize(DevSuiteContext context)
        {
            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= UpdateFrame;
            }

            _context = context;

            if (_context != null)
            {
                _context.OnChanged += HandleContextChanged;
                _context.OnEveryFrame += UpdateFrame;
                UpdateInspector();
            }
        }

        public void Reset()
        {
            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= UpdateFrame;
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

            var currentSelect = _context.SelectedGameObject;
            if (currentSelect == _lastSelectedGameObject)
            {
                if (currentSelect == null && _lastSelectedGameObject != null)
                {
                    RebuildInspector(null);
                }
                return;
            }

            RebuildInspector(currentSelect);
        }

        private void RebuildInspector(GameObject go)
        {
            _lastSelectedGameObject = go;
            _scrollView.Clear();
            _trackedProperties.Clear();

            if (go == null)
            {
                _selectedObjectLabel.text = "None selected";
                _selectedObjectPathLabel.text = "";
                return;
            }

            _selectedObjectLabel.text = go.name;
            _selectedObjectPathLabel.text = GetGameObjectPath(go);

            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null)
                {
                    continue; // missing scripts
                }

                RenderComponent(comp);
            }
        }

        private void RenderComponent(Component comp)
        {
            var compType = comp.GetType();

            var compBox = new VisualElement
            {
                name = "compBox",
            };
            compBox.AddToClassList("inspector-component-box");
            _scrollView.Add(compBox);

            var header = new VisualElement
            {
                name = "compHeader",
            };
            header.AddToClassList("inspector-component-header");
            compBox.Add(header);

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

            var body = new VisualElement
            {
                name = "compBody",
            };
            body.AddToClassList("inspector-component-body");
            compBox.Add(body);

            var allFields = compType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var allProps = compType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var serializableFields = new List<FieldInfo>();
            var otherMembers = new List<MemberInfo>();

            foreach (var field in allFields)
            {
                if (field.Name.StartsWith("<"))
                {
                    continue;
                }
                if (field.GetCustomAttribute<ObsoleteAttribute>() != null)
                {
                    continue;
                }

                if (IsSerializable(field))
                {
                    serializableFields.Add(field);
                }
                else
                {
                    otherMembers.Add(field);
                }
            }

            foreach (var prop in allProps)
            {
                if (!prop.CanRead)
                {
                    continue;
                }
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (prop.GetCustomAttribute<ObsoleteAttribute>() != null)
                {
                    continue;
                }

                if (prop.DeclaringType == typeof(Component) ||
                    prop.DeclaringType == typeof(MonoBehaviour) ||
                    prop.DeclaringType == typeof(Behaviour) ||
                    prop.DeclaringType == typeof(UnityEngine.Object))
                {
                    continue;
                }

                otherMembers.Add(prop);
            }

            foreach (var field in serializableFields)
            {
                RenderPropertyRow(comp, field, true, body);
            }

            foreach (var member in otherMembers)
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
                    _ => null
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
                    ? "null" :
                    $"[{obj.GetType().Name}] {obj.name}";
            }

            return value.ToString();
        }

        private void UpdateFrame()
        {
            if (_context == null || _lastSelectedGameObject == null)
            {
                return;
            }

            if (_lastSelectedGameObject == null || _lastSelectedGameObject.Equals(null))
            {
                RebuildInspector(null);
                return;
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
                        _ => null
                    };

                    prop.ValueLabel.text = FormatValue(val);
                }
                catch
                {
                    prop.ValueLabel.text = "<error>";
                }
            }
        }

        private string GetGameObjectPath(GameObject go)
        {
            if (go == null)
            {
                return "";
            }
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return go.scene.name + "/" + path;
        }
    }
}