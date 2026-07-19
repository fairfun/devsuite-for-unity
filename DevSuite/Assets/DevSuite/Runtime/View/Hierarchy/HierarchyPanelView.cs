using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ff.DevSuite.View
{
    internal class HierarchyPanelView : VisualElement
    {
        private DevSuiteContext _context;

        private readonly Button _pickBtn;
        private readonly Button _refreshBtn;
        private readonly TextField _filterField;
        private readonly Button _prevBtn;
        private readonly Button _nextBtn;
        private readonly Toggle _regexToggle;
        private readonly Toggle _nameToggle;
        private readonly Toggle _typeToggle;
        private readonly Toggle _dimToggle;
        private readonly ScrollView _scrollView;

        private readonly HashSet<string> _collapsedSceneNames = new();
        private readonly HashSet<int> _expandedGameObjectInstanceIds = new();

        private readonly HashSet<int> _matchingInstanceIds = new();
        private readonly HashSet<int> _descendantMatchingInstanceIds = new();
        private readonly Dictionary<int, VisualElement> _gameObjectRows = new();

        private bool _pickModeActive;
        private Regex _searchRegex;

        public HierarchyPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            AddToClassList("ff-panel");

            var root = this.Q<VisualElement>("hierarchy-panel-root") ?? this;

            _pickBtn = root.Q<Button>("pickBtn");
            _pickBtn.text = "\uf05b"; // crosshairs
            _pickBtn.clicked += TogglePickMode;

            _refreshBtn = root.Q<Button>("refreshBtn");
            _refreshBtn.text = "\uf021"; // sync
            _refreshBtn.clicked += () =>
            {
                RebuildTree();
                DevSuiteUtils.ShowIconButtonClickedFeedback(_refreshBtn);
            };

            _filterField = root.Q<TextField>("filterField");
            DevSuiteUtils.SetupInputFieldFocus(_filterField);
            _filterField.RegisterValueChangedCallback(evt => HandleSearchChanged(evt.newValue));

            _prevBtn = root.Q<Button>("prevBtn");
            _prevBtn.text = "\uf104"; // angle-left
            _prevBtn.clicked += HandlePrevResult;

            _nextBtn = root.Q<Button>("nextBtn");
            _nextBtn.text = "\uf105"; // angle-right
            _nextBtn.clicked += HandleNextResult;

            _regexToggle = root.Q<Toggle>("regexToggle");
            _regexToggle.value = false;
            _regexToggle.RegisterValueChangedCallback(_ => HandleSearchOptionsChanged());
            SetupCheckbox(_regexToggle);

            _nameToggle = root.Q<Toggle>("nameToggle");
            _nameToggle.value = true;
            _nameToggle.RegisterValueChangedCallback(_ => HandleSearchOptionsChanged());
            SetupCheckbox(_nameToggle);

            _typeToggle = root.Q<Toggle>("typeToggle");
            _typeToggle.value = true;
            _typeToggle.RegisterValueChangedCallback(_ => HandleSearchOptionsChanged());
            SetupCheckbox(_typeToggle);

            _dimToggle = root.Q<Toggle>("dimToggle");
            _dimToggle.value = true;
            _dimToggle.RegisterValueChangedCallback(_ => HandleSearchOptionsChanged());
            SetupCheckbox(_dimToggle);

            _scrollView = root.Q<ScrollView>("hierarchyScrollView");
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
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
                RebuildTree();
            }
        }

        public void Reset()
        {
            SetPickMode(false);

            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= UpdateFrame;
                _context = null;
            }
        }

        private void HandleContextChanged()
        {
            // Update highlight for selection
            UpdateSelectionHighlight();
        }

        private void TogglePickMode()
        {
            SetPickMode(!_pickModeActive);
        }

        private void SetPickMode(bool active)
        {
            _pickModeActive = active;
            _pickBtn.EnableInClassList("active", _pickModeActive);
        }

        private void HandleSearchChanged(string query)
        {
            UpdateSearchRegex(query);
            PrecomputeSearch();
            RebuildTree();
        }

        private void HandleSearchOptionsChanged()
        {
            UpdateSearchRegex(_filterField.value);
            PrecomputeSearch();
            RebuildTree();
        }

        private void UpdateSearchRegex(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                _searchRegex = null;
                return;
            }

            if (_regexToggle.value)
            {
                try
                {
                    _searchRegex = new Regex(query, RegexOptions.IgnoreCase);
                }
                catch
                {
                    _searchRegex = DevSuiteUtils.NeverMatch;
                }
            }
            else
            {
                _searchRegex = DevSuiteUtils.GetSmartSearchRegex(query);
            }
        }

        private void PrecomputeSearch()
        {
            _matchingInstanceIds.Clear();
            _descendantMatchingInstanceIds.Clear();

            if (_searchRegex == null)
            {
                return;
            }

            var searchByName = _nameToggle.value;
            var searchByType = _typeToggle.value;

            // If neither is checked, default to name search
            if (!searchByName && !searchByType)
            {
                searchByName = true;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (var go in scene.GetRootGameObjects())
                {
                    CheckMatchesRecursive(go, _searchRegex, searchByName, searchByType);
                }
            }
        }

        private bool CheckMatchesRecursive(GameObject go, Regex regex, bool searchByName, bool searchByType)
        {
            if (go == null)
            {
                return false;
            }

            var selfMatches = Matches(go, regex, searchByName, searchByType);
            if (selfMatches)
            {
                _matchingInstanceIds.Add(go.GetInstanceID());
            }

            var anyChildMatches = false;
            for (var i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                if (child != null && CheckMatchesRecursive(child.gameObject, regex, searchByName, searchByType))
                {
                    anyChildMatches = true;
                }
            }

            if (anyChildMatches)
            {
                _descendantMatchingInstanceIds.Add(go.GetInstanceID());
            }

            return selfMatches || anyChildMatches;
        }

        private bool Matches(GameObject go, Regex regex, bool searchByName, bool searchByType)
        {
            if (searchByName)
            {
                if (regex.IsMatch(go.name))
                {
                    return true;
                }
            }

            if (searchByType)
            {
                var components = go.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp != null && regex.IsMatch(comp.GetType().Name))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RebuildTree()
        {
            _scrollView.Clear();
            _gameObjectRows.Clear();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                RenderSceneNode(scene);
            }

            UpdateSelectionHighlight();
        }

        private void RenderSceneNode(Scene scene)
        {
            var sceneName = scene.name;
            var container = new VisualElement
            {
                name = "sceneContainer",
            };
            _scrollView.Add(container);

            var row = new VisualElement();
            row.AddToClassList("hierarchy-item-row");
            row.AddToClassList("hierarchy-scene-row");

            var foldoutBtn = new Button
            {
                name = "foldoutBtn",
            };
            foldoutBtn.AddToClassList("hierarchy-foldout-btn");

            bool isExpanded = !_collapsedSceneNames.Contains(sceneName);
            foldoutBtn.text = isExpanded ? "\uf0d7" : "\uf0da"; // caret down / caret right
            row.Add(foldoutBtn);

            var label = new Label
            {
                name = "itemLabel",
                text = $"Scene: {sceneName}",
            };
            label.AddToClassList("hierarchy-item-label");
            row.Add(label);

            container.Add(row);

            var childrenContainer = new VisualElement
            {
                name = "sceneChildren",
            };
            childrenContainer.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            container.Add(childrenContainer);

            foldoutBtn.clicked += () =>
            {
                if (!_collapsedSceneNames.Contains(sceneName))
                {
                    _collapsedSceneNames.Add(sceneName);
                    foldoutBtn.text = "\uf0da";
                    childrenContainer.style.display = DisplayStyle.None;
                }
                else
                {
                    _collapsedSceneNames.Remove(sceneName);
                    foldoutBtn.text = "\uf0d7";
                    childrenContainer.style.display = DisplayStyle.Flex;
                }
            };

            var rootObjects = scene.GetRootGameObjects();
            foreach (var go in rootObjects)
            {
                RenderGameObjectNode(go, 1, childrenContainer);
            }
        }

        private void RenderGameObjectNode(GameObject go, int depth, VisualElement container)
        {
            if (go == null)
            {
                return;
            }

            var instanceId = go.GetInstanceID();
            var isMatching = _searchRegex == null || _matchingInstanceIds.Contains(instanceId);
            var hasMatchingDescendant = _searchRegex == null || _descendantMatchingInstanceIds.Contains(instanceId);

            // Hide mode: If search active, hide if it doesn't match and has no matching descendants
            if (_searchRegex != null && !_dimToggle.value && !isMatching && !hasMatchingDescendant)
            {
                return;
            }

            var nodeContainer = new VisualElement
            {
                name = "nodeContainer",
            };
            container.Add(nodeContainer);

            var row = new VisualElement();
            row.AddToClassList("hierarchy-item-row");
            row.AddToClassList("hierarchy-object-row");
            if (!go.activeSelf)
            {
                row.AddToClassList("inactive");
            }
            row.style.paddingLeft = 6 + (depth * 16);

            if (_searchRegex != null && _dimToggle.value)
            {
                if (isMatching || hasMatchingDescendant)
                {
                    row.RemoveFromClassList("dimmed");
                }
                else
                {
                    row.AddToClassList("dimmed");
                }
            }

            _gameObjectRows[instanceId] = row;

            var foldoutBtn = new Button
            {
                name = "foldoutBtn",
            };
            foldoutBtn.AddToClassList("hierarchy-foldout-btn");

            var hasChildren = go.transform.childCount > 0;
            var isExpanded = _expandedGameObjectInstanceIds.Contains(instanceId) || (_searchRegex != null && hasMatchingDescendant);

            if (hasChildren)
            {
                foldoutBtn.text = isExpanded ? "\uf0d7" : "\uf0da";
            }
            else
            {
                foldoutBtn.text = "";
                foldoutBtn.style.visibility = Visibility.Hidden;
            }
            row.Add(foldoutBtn);

            var label = new Label
            {
                name = "itemLabel",
                text = go.name,
            };
            label.AddToClassList("hierarchy-item-label");
            row.Add(label);

            nodeContainer.Add(row);

            var childrenContainer = new VisualElement
            {
                name = "nodeChildren",
            };
            childrenContainer.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            nodeContainer.Add(childrenContainer);

            foldoutBtn.clicked += () =>
            {
                if (_expandedGameObjectInstanceIds.Contains(instanceId))
                {
                    _expandedGameObjectInstanceIds.Remove(instanceId);
                    foldoutBtn.text = "\uf0da";
                    childrenContainer.style.display = DisplayStyle.None;
                }
                else
                {
                    _expandedGameObjectInstanceIds.Add(instanceId);
                    foldoutBtn.text = "\uf0d7";
                    childrenContainer.style.display = DisplayStyle.Flex;
                }
            };

            row.RegisterCallback<ClickEvent>(
                evt =>
                {
                    if (_context != null)
                    {
                        _context.SelectedGameObject = go;
                    }
                }
            );

            if (hasChildren)
            {
                for (var i = 0; i < go.transform.childCount; i++)
                {
                    RenderGameObjectNode(go.transform.GetChild(i).gameObject, depth + 1, childrenContainer);
                }
            }
        }

        private void UpdateSelectionHighlight()
        {
            foreach (var kvp in _gameObjectRows)
            {
                kvp.Value.RemoveFromClassList("selected");
            }

            if (_context != null && _context.SelectedGameObject != null)
            {
                var selId = _context.SelectedGameObject.GetInstanceID();
                if (_gameObjectRows.TryGetValue(selId, out var row))
                {
                    row.AddToClassList("selected");
                }
            }
        }

        private void HandlePrevResult()
        {
            NavigateSearchResults(-1);
        }

        private void HandleNextResult()
        {
            NavigateSearchResults(1);
        }

        private void NavigateSearchResults(int direction)
        {
            PrecomputeSearch();
            if (_matchingInstanceIds.Count == 0)
            {
                return;
            }

            var list = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (var go in scene.GetRootGameObjects())
                {
                    CollectMatchingObjectsRecursive(go, list);
                }
            }

            if (list.Count == 0)
            {
                return;
            }

            var currentIndex = -1;
            if (_context.SelectedGameObject != null)
            {
                currentIndex = list.FindIndex(go => go == _context.SelectedGameObject);
            }

            int nextIndex;
            if (currentIndex == -1)
            {
                nextIndex = direction > 0 ? 0 : list.Count - 1;
            }
            else
            {
                nextIndex = (currentIndex + direction + list.Count) % list.Count;
            }

            var target = list[nextIndex];
            _context.SelectedGameObject = target;
            ExpandParents(target);
            RebuildTree();

            if (_gameObjectRows.TryGetValue(target.GetInstanceID(), out var row))
            {
                _scrollView.ScrollTo(row);
            }
        }

        private void CollectMatchingObjectsRecursive(GameObject go, List<GameObject> list)
        {
            if (go == null)
            {
                return;
            }
            if (_matchingInstanceIds.Contains(go.GetInstanceID()))
            {
                list.Add(go);
            }

            for (var i = 0; i < go.transform.childCount; i++)
            {
                CollectMatchingObjectsRecursive(go.transform.GetChild(i).gameObject, list);
            }
        }

        private void ExpandParents(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            _collapsedSceneNames.Remove(go.scene.name);

            var parent = go.transform.parent;
            while (parent != null)
            {
                _expandedGameObjectInstanceIds.Add(parent.gameObject.GetInstanceID());
                parent = parent.parent;
            }
        }

        private void UpdateFrame()
        {
            UpdatePickMode();
        }

        private void UpdatePickMode()
        {
            if (!_pickModeActive)
            {
                return;
            }

            var clicked = false;
            var mousePos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                clicked = UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
                mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            }
#else
            clicked = Input.GetMouseButtonDown(0);
            mousePos = Input.mousePosition;
#endif

            if (clicked)
            {
                var panelPos = RuntimePanelUtils.ScreenToPanel(panel, mousePos);
                var pickedUi = panel.Pick(panelPos);

                if (pickedUi != null && IsElementInDevSuite(pickedUi))
                {
                    return;
                }

                var cam = Camera.main;
                if (cam == null)
                {
                    cam = Object.FindObjectOfType<Camera>();
                }

                if (cam != null)
                {
                    var ray = cam.ScreenPointToRay(mousePos);
                    GameObject pickedObj = null;

                    if (Physics.Raycast(ray, out var hit))
                    {
                        pickedObj = hit.collider.gameObject;
                    }
                    else
                    {
                        var hit2d = Physics2D.GetRayIntersection(ray);
                        if (hit2d.collider != null)
                        {
                            pickedObj = hit2d.collider.gameObject;
                        }
                    }

                    if (pickedObj != null)
                    {
                        _context.SelectedGameObject = pickedObj;
                        ExpandParents(pickedObj);
                        RebuildTree();

                        if (_gameObjectRows.TryGetValue(pickedObj.GetInstanceID(), out var row))
                        {
                            _scrollView.ScrollTo(row);
                        }

                        SetPickMode(false);
                    }
                }
            }
        }

        private bool IsElementInDevSuite(VisualElement element)
        {
            var cur = element;
            while (cur != null)
            {
                if (cur.ClassListContains("devsuite-panel-root") || cur.ClassListContains("ff-control-panel") || cur.name == "ff-control-panel-root")
                {
                    return true;
                }
                cur = cur.parent;
            }
            return false;
        }

        private void SetupCheckbox(Toggle toggle)
        {
            toggle.AddToClassList("hierarchy-toggle");
            var checkmark = toggle.Q<VisualElement>("unity-checkmark");
            if (checkmark != null)
            {
                var icon = new Label
                {
                    text = "\uf00c",
                };
                icon.AddToClassList("hierarchy-toggle-icon");
                checkmark.Add(icon);
            }
        }
    }
}