using System.Collections.Generic;
using System.Linq;
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
        private readonly Button _copyBtn;
        private readonly TextField _filterField;
        private readonly Button _prevBtn;
        private readonly Button _nextBtn;
        private readonly Button _regexBtn;
        private readonly Button _nameBtn;
        private readonly Button _typeBtn;
        private readonly Button _dimBtn;

        private bool _searchByRegex = false;
        private bool _searchByName = true;
        private bool _searchByType = true;
        private bool _keepDimmed = true;
        private readonly ScrollView _scrollView;

        private readonly HashSet<string> _collapsedSceneNames = new();
        private readonly HashSet<int> _expandedGameObjectInstanceIds = new();

        private readonly HashSet<int> _matchingInstanceIds = new();
        private readonly HashSet<int> _descendantMatchingInstanceIds = new();
        private readonly Dictionary<int, VisualElement> _gameObjectRows = new();
        private readonly List<VisualElement> _currentlySelectedRows = new();
        private GameObject _selectionAnchor;

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

            _copyBtn = root.Q<Button>("copyBtn");
            if (_copyBtn != null)
            {
                _copyBtn.text = "\uf0c5"; // copy icon
                _copyBtn.clicked += () =>
                {
                    var hierarchyText = GetFullHierarchyAsText();
                    DevSuiteUtils.CopyToClipboard(hierarchyText);
                    DevSuiteUtils.ShowIconButtonClickedFeedback(_copyBtn);
                };
            }

            _filterField = root.Q<TextField>("filterField");
            DevSuiteUtils.SetupInputFieldFocus(_filterField);
            _filterField.RegisterValueChangedCallback(evt => HandleSearchChanged(evt.newValue));

            _prevBtn = root.Q<Button>("prevBtn");
            _prevBtn.text = "\uf104"; // angle-left
            _prevBtn.clicked += HandlePrevResult;

            _nextBtn = root.Q<Button>("nextBtn");
            _nextBtn.text = "\uf105"; // angle-right
            _nextBtn.clicked += HandleNextResult;

            _regexBtn = root.Q<Button>("regexBtn");
            _regexBtn.text = ".*";
            _regexBtn.clicked += () =>
            {
                _searchByRegex = !_searchByRegex;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _nameBtn = root.Q<Button>("nameBtn");
            _nameBtn.text = "\uf02b"; // tag
            _nameBtn.clicked += () =>
            {
                _searchByName = !_searchByName;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _typeBtn = root.Q<Button>("typeBtn");
            _typeBtn.text = "\uf1b2"; // cube
            _typeBtn.clicked += () =>
            {
                _searchByType = !_searchByType;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _dimBtn = root.Q<Button>("dimBtn");
            _dimBtn.text = "\uf042"; // adjust
            _dimBtn.clicked += () =>
            {
                _keepDimmed = !_keepDimmed;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            UpdateButtonStates();

            _scrollView = root.Q<ScrollView>("hierarchyScrollView");
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            DevSuiteUtils.SetupTooltips(this);

            RegisterCallback<AttachToPanelEvent>(
                evt =>
                {
#if UNITY_EDITOR
                    UnityEditor.Selection.selectionChanged += HandleEditorSelectionChanged;
                    // Sync initial selection
                    if (_context != null)
                    {
                        _context.SelectedGameObject = UnityEditor.Selection.activeGameObject;
                    }
#endif
                }
            );

            RegisterCallback<DetachFromPanelEvent>(
                evt =>
                {
#if UNITY_EDITOR
                    UnityEditor.Selection.selectionChanged -= HandleEditorSelectionChanged;
#endif
                }
            );
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
                RebuildTree();
            }
        }

        public void Reset()
        {
            SetPickMode(false);

            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= HandleOnEveryFrame;
                _context = null;
            }
        }

        private void HandleContextChanged()
        {
            if (_context != null && _context.SelectedGameObject != null)
            {
                var targetId = _context.SelectedGameObject.GetInstanceID();
                if (!_gameObjectRows.ContainsKey(targetId))
                {
                    ExpandParents(_context.SelectedGameObject);
                    RebuildTree();
                    if (_gameObjectRows.TryGetValue(targetId, out var row))
                    {
                        _scrollView.ScrollTo(row);
                    }
                }
            }
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

            if (_searchByRegex)
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

            var searchByName = _searchByName;
            var searchByType = _searchByType;

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
            _currentlySelectedRows.Clear();

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

            var isExpanded = !_collapsedSceneNames.Contains(sceneName);
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

            row.RegisterCallback<ClickEvent>(
                evt =>
                {
                    if (evt.clickCount == 2)
                    {
                        if (!_collapsedSceneNames.Contains(sceneName))
                        {
                            _collapsedSceneNames.Add(sceneName);
                        }
                        else
                        {
                            _collapsedSceneNames.Remove(sceneName);
                        }
                        RebuildTree();
                        evt.StopPropagation();
                    }
                }
            );

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
            if (_searchRegex != null && !_keepDimmed && !isMatching && !hasMatchingDescendant)
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

            if (_searchRegex != null && _keepDimmed)
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
                    if (evt.clickCount == 2 && hasChildren)
                    {
                        if (_expandedGameObjectInstanceIds.Contains(instanceId))
                        {
                            _expandedGameObjectInstanceIds.Remove(instanceId);
                        }
                        else
                        {
                            _expandedGameObjectInstanceIds.Add(instanceId);
                        }
                        RebuildTree();
                        evt.StopPropagation();
                    }
                    else if (evt.clickCount == 1)
                    {
                        if (_context != null)
                        {
                            var isCtrlHeld = evt.ctrlKey || evt.commandKey;
                            var isShiftHeld = evt.shiftKey;

                            if (isShiftHeld && _selectionAnchor != null)
                            {
                                var visibleList = GetVisibleGameObjectsInOrder();
                                if (visibleList.Contains(_selectionAnchor) && visibleList.Contains(go))
                                {
                                    var anchorIndex = visibleList.IndexOf(_selectionAnchor);
                                    var targetIndex = visibleList.IndexOf(go);
                                    var start = Mathf.Min(anchorIndex, targetIndex);
                                    var end = Mathf.Max(anchorIndex, targetIndex);

                                    var range = new List<GameObject>();
                                    for (var i = start; i <= end; i++)
                                    {
                                        range.Add(visibleList[i]);
                                    }

                                    _context.SetSelectedGameObjects(range);
#if UNITY_EDITOR
                                    UnityEditor.Selection.objects = range.ToArray();
#endif
                                }
                            }
                            else
                            {
                                _selectionAnchor = go;
                                if (isCtrlHeld)
                                {
                                    _context.ToggleSelectedGameObject(go);
                                }
                                else
                                {
                                    _context.SelectedGameObject = go;
                                }
#if UNITY_EDITOR
                                if (isCtrlHeld)
                                {
                                    var currentSelection = new List<Object>(UnityEditor.Selection.objects);
                                    if (currentSelection.Contains(go))
                                    {
                                        currentSelection.Remove(go);
                                    }
                                    else
                                    {
                                        currentSelection.Add(go);
                                    }
                                    UnityEditor.Selection.objects = currentSelection.ToArray();
                                }
                                else
                                {
                                    UnityEditor.Selection.activeGameObject = go;
                                }
#endif
                            }
                        }
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
            foreach (var row in _currentlySelectedRows)
            {
                if (row != null)
                {
                    row.RemoveFromClassList("selected");
                }
            }
            _currentlySelectedRows.Clear();

            if (_context != null)
            {
                foreach (var go in _context.SelectedGameObjects)
                {
                    if (go == null)
                    {
                        continue;
                    }
                    var selId = go.GetInstanceID();
                    if (_gameObjectRows.TryGetValue(selId, out var row))
                    {
                        row.AddToClassList("selected");
                        _currentlySelectedRows.Add(row);
                    }
                }

                if (_selectionAnchor == null || !_context.SelectedGameObjects.Contains(_selectionAnchor))
                {
                    _selectionAnchor = _context.SelectedGameObject;
                }
            }
        }

        private List<GameObject> GetVisibleGameObjectsInOrder()
        {
            var visibleList = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                if (_collapsedSceneNames.Contains(scene.name))
                {
                    continue;
                }

                var rootObjects = scene.GetRootGameObjects();
                foreach (var go in rootObjects)
                {
                    GetVisibleChildrenRecursive(go, visibleList);
                }
            }
            return visibleList;
        }

        private void GetVisibleChildrenRecursive(GameObject go, List<GameObject> visibleList)
        {
            if (go == null)
            {
                return;
            }

            var matches = true;
            if (_searchRegex != null)
            {
                matches = _matchingInstanceIds.Contains(go.GetInstanceID()) || _descendantMatchingInstanceIds.Contains(go.GetInstanceID());
            }

            if (matches)
            {
                visibleList.Add(go);
            }

            var instanceId = go.GetInstanceID();
            if (_expandedGameObjectInstanceIds.Contains(instanceId))
            {
                for (var i = 0; i < go.transform.childCount; i++)
                {
                    GetVisibleChildrenRecursive(go.transform.GetChild(i).gameObject, visibleList);
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

        private void HandleOnEveryFrame()
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

        private void UpdateButtonStates()
        {
            _regexBtn.EnableInClassList("active", _searchByRegex);
            _nameBtn.EnableInClassList("active", _searchByName);
            _typeBtn.EnableInClassList("active", _searchByType);
            _dimBtn.EnableInClassList("active", _keepDimmed);
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

        private string GetFullHierarchyAsText()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                sb.AppendLine($"{scene.name} (scene)");
                var rootGameObjects = scene.GetRootGameObjects();
                foreach (var rootGo in rootGameObjects)
                {
                    FormatGameObjectNodeRecursive(rootGo, 1, sb);
                }
            }
            return sb.ToString();
        }

        private void FormatGameObjectNodeRecursive(GameObject go, int depth, System.Text.StringBuilder sb)
        {
            if (go == null)
            {
                return;
            }

            var indent = new string(' ', depth * 2);
            var components = go.GetComponents<Component>();
            var typeNames = new List<string>();
            foreach (var comp in components)
            {
                if (comp == null)
                {
                    continue;
                }
                var typeName = comp.GetType().Name;
                if (!typeNames.Contains(typeName))
                {
                    typeNames.Add(typeName);
                }
            }

            var typesStr = typeNames.Count > 0 ? $" ({string.Join(", ", typeNames)})" : "";
            sb.AppendLine($"{indent}{go.name}{typesStr}");

            for (var i = 0; i < go.transform.childCount; i++)
            {
                FormatGameObjectNodeRecursive(go.transform.GetChild(i).gameObject, depth + 1, sb);
            }
        }

#if UNITY_EDITOR
        private void HandleEditorSelectionChanged()
        {
            if (_context != null)
            {
                var newSelection = UnityEditor.Selection.gameObjects;
                var selectionChanged = false;
                if (_context.SelectedGameObjects.Count != newSelection.Length)
                {
                    selectionChanged = true;
                }
                else
                {
                    foreach (var selection in newSelection)
                    {
                        if (!_context.SelectedGameObjects.Contains(selection))
                        {
                            selectionChanged = true;
                            break;
                        }
                    }
                }

                if (selectionChanged)
                {
                    _context.SetSelectedGameObjects(newSelection);
                }
            }
        }
#endif

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
    }
}