using System;
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
        private readonly Dictionary<int, (GameObject Go, Toggle Toggle)> _gameObjectActivityToggles = new();
        private readonly List<VisualElement> _currentlySelectedRows = new();
        private GameObject _selectionAnchor;

        private bool _pickModeActive;
        private float? _previousTimeScale;
        private Regex _searchRegex;

        private struct PickTarget
        {
            public GameObject GameObject;
            public string Kind;
        }

        private VisualElement _pickPopup;
        private ScrollView _pickPopupScrollView;
        private StyleSheet _uss;

        public HierarchyPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            _uss = uss;
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            AddToClassList("ff-panel");
            RegisterCallback<DetachFromPanelEvent>(_ => SetPickMode(false));

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
                if (_context != null)
                {
                    _context.HierarchySearchRegex = _searchByRegex;
                }
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _nameBtn = root.Q<Button>("nameBtn");
            _nameBtn.text = "\uf02b"; // tag
            _nameBtn.clicked += () =>
            {
                _searchByName = !_searchByName;
                if (_context != null)
                {
                    _context.HierarchySearchByName = _searchByName;
                }
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _typeBtn = root.Q<Button>("typeBtn");
            _typeBtn.text = "\uf1b2"; // cube
            _typeBtn.clicked += () =>
            {
                _searchByType = !_searchByType;
                if (_context != null)
                {
                    _context.HierarchySearchByType = _searchByType;
                }
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _dimBtn = root.Q<Button>("dimBtn");
            _dimBtn.text = "\uf042"; // adjust
            _dimBtn.clicked += () =>
            {
                _keepDimmed = !_keepDimmed;
                if (_context != null)
                {
                    _context.HierarchyKeepDimmed = _keepDimmed;
                }
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

                _searchByRegex = _context.HierarchySearchRegex;
                _searchByName = _context.HierarchySearchByName;
                _searchByType = _context.HierarchySearchByType;
                _keepDimmed = _context.HierarchyKeepDimmed;
                UpdateButtonStates();
                UpdateSearchRegex(_filterField.value);
                PrecomputeSearch();
                RebuildTree();
            }
        }

        public void Reset()
        {
            SetPickMode(false);
            HidePickPopup();

            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= HandleOnEveryFrame;
                _context = null;
            }
        }

        private void HandleContextChanged()
        {
            if (_context != null)
            {
                if ((!_context.PanelExpanded || !_context.HierarchyVisible) && _pickModeActive)
                {
                    SetPickMode(false);
                }

                var regex = _context.HierarchySearchRegex;
                var name = _context.HierarchySearchByName;
                var type = _context.HierarchySearchByType;
                var dim = _context.HierarchyKeepDimmed;
                if (regex != _searchByRegex || name != _searchByName || type != _searchByType || dim != _keepDimmed)
                {
                    _searchByRegex = regex;
                    _searchByName = name;
                    _searchByType = type;
                    _keepDimmed = dim;
                    UpdateButtonStates();
                    HandleSearchOptionsChanged();
                }
            }

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
            if (_pickModeActive == active)
            {
                return;
            }

            _pickModeActive = active;
            _pickBtn.EnableInClassList("active", _pickModeActive);

            if (_pickModeActive)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                HidePickPopup();
                if (_previousTimeScale.HasValue)
                {
                    Time.timeScale = _previousTimeScale.Value;
                    _previousTimeScale = null;
                }
            }
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

            if (!searchByName && !searchByType)
            {
                return;
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
            _gameObjectActivityToggles.Clear();
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

            var activityToggle = new Toggle
            {
                name = "activityToggle",
                value = go.activeSelf,
                tooltip = "Toggle active state"
            };
            activityToggle.AddToClassList("ff-toggle");
            activityToggle.AddToClassList("hierarchy-activity-toggle");
            var activityCheckmark = activityToggle.Q<VisualElement>("unity-checkmark");
            if (activityCheckmark != null)
            {
                var icon = new Label("\uf00c");
                icon.AddToClassList("ff-toggle-icon");
                activityCheckmark.Add(icon);
            }
            activityToggle.RegisterValueChangedCallback(evt =>
            {
                if (go != null)
                {
                    go.SetActive(evt.newValue);
                    if (evt.newValue)
                    {
                        row.RemoveFromClassList("inactive");
                    }
                    else
                    {
                        row.AddToClassList("inactive");
                    }
                }
            });
            var label = new Label
            {
                name = "itemLabel",
                text = go.name,
            };
            label.AddToClassList("hierarchy-item-label");
            row.Add(label);

            row.Add(activityToggle);
            _gameObjectActivityToggles[instanceId] = (go, activityToggle);

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
            SyncActivityStates();
        }

        private void SyncActivityStates()
        {
            foreach (var kvp in _gameObjectActivityToggles)
            {
                var (go, toggle) = kvp.Value;
                if (go == null || toggle == null || toggle.panel == null) continue;

                var row = _gameObjectRows.TryGetValue(kvp.Key, out var r) ? r : null;
                if (row == null) continue;

                bool isActive = go.activeSelf;
                if (toggle.value != isActive)
                {
                    toggle.SetValueWithoutNotify(isActive);
                    if (isActive) row.RemoveFromClassList("inactive");
                    else row.AddToClassList("inactive");
                }
            }
        }

        private void UpdatePickMode()
        {
            if (!_pickModeActive)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetPickMode(false);
                return;
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetPickMode(false);
                return;
            }
#endif

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
                var topRoot = DevSuiteUtils.GetTopRoot(this) ?? this;
                float screenHeight = Screen.height > 0 ? Screen.height : 600f;
                float screenWidth = Screen.width > 0 ? Screen.width : 800f;
                float panelWidth = (topRoot?.layout.width > 0) ? topRoot.layout.width : (topRoot?.resolvedStyle.width > 0 ? topRoot.resolvedStyle.width : screenWidth);
                float panelHeight = (topRoot?.layout.height > 0) ? topRoot.layout.height : (topRoot?.resolvedStyle.height > 0 ? topRoot.resolvedStyle.height : screenHeight);

                var panelPos = new Vector2(
                    mousePos.x * (panelWidth / screenWidth),
                    (screenHeight - mousePos.y) * (panelHeight / screenHeight)
                );

                // 1. If click is inside the active pick popup, ignore it in pick mode so the popup button handles the click
                if (_pickPopup != null && _pickPopup.style.display != DisplayStyle.None && _pickPopup.parent != null)
                {
                    if (_pickPopup.worldBound.Contains(panelPos))
                    {
                        return;
                    }
                }

                // 2. If click is on an interactive DevSuite UI element (e.g. pick toggle button), close popup and let DevSuite handle it
                var pickedUi = panel?.Pick(panelPos);
                if (pickedUi != null && IsElementInteractiveInDevSuite(pickedUi))
                {
                    HidePickPopup();
                    return;
                }

                // 3. User clicked in the game view to pick objects!
                var targets = CollectPickTargets(mousePos);
                if (targets.Count > 0)
                {
                    ShowPickPopup(targets, panelPos);
                }
                else
                {
                    HidePickPopup();
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
            var disabledStr = !go.activeSelf ? " (inactive)" : "";
            sb.AppendLine($"{indent}{go.name}{typesStr}{disabledStr}");

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

        private bool IsElementInteractiveInDevSuite(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }
            if (!IsElementInDevSuite(element))
            {
                return false;
            }

            var cur = element;
            while (cur != null)
            {
                if (cur == _pickPopup)
                {
                    return true;
                }

                if (cur is Button || cur is TextField || cur is Toggle || cur is Scroller || cur is Slider)
                {
                    return true;
                }

                var type = cur.GetType();
                while (type != null && type != typeof(VisualElement))
                {
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseField<>))
                    {
                        return true;
                    }
                    type = type.BaseType;
                }

                cur = cur.parent;
            }
            return false;
        }

        private bool IsGameObjectInDevSuite(GameObject go)
        {
            if (go == null)
            {
                return false;
            }
            var cur = go.transform;
            while (cur != null)
            {
                if (cur.name.Contains("DevSuitePanel") || cur.GetComponent<DevSuitePanelUI>() != null)
                {
                    return true;
                }
                cur = cur.parent;
            }
            return false;
        }

        private void CreatePickPopup()
        {
            _pickPopup = new VisualElement();
            if (_uss != null)
            {
                _pickPopup.styleSheets.Add(_uss);
            }
            _pickPopup.AddToClassList("hierarchy-pick-popup");
            _pickPopup.style.position = Position.Absolute;
            _pickPopup.style.backgroundColor = new Color(26 / 255f, 26 / 255f, 30 / 255f, 0.95f);
            _pickPopup.style.borderLeftColor = new Color(80 / 255f, 80 / 255f, 80 / 255f, 0.8f);
            _pickPopup.style.borderRightColor = new Color(80 / 255f, 80 / 255f, 80 / 255f, 0.8f);
            _pickPopup.style.borderTopColor = new Color(80 / 255f, 80 / 255f, 80 / 255f, 0.8f);
            _pickPopup.style.borderBottomColor = new Color(80 / 255f, 80 / 255f, 80 / 255f, 0.8f);
            _pickPopup.style.borderLeftWidth = 1;
            _pickPopup.style.borderRightWidth = 1;
            _pickPopup.style.borderTopWidth = 1;
            _pickPopup.style.borderBottomWidth = 1;
            _pickPopup.style.borderTopLeftRadius = 6;
            _pickPopup.style.borderTopRightRadius = 6;
            _pickPopup.style.borderBottomLeftRadius = 6;
            _pickPopup.style.borderBottomRightRadius = 6;
            _pickPopup.style.paddingLeft = 3;
            _pickPopup.style.paddingRight = 3;
            _pickPopup.style.paddingTop = 3;
            _pickPopup.style.paddingBottom = 3;
            _pickPopup.style.minWidth = 180;
            _pickPopup.style.maxWidth = 340;
            _pickPopup.style.maxHeight = 390;
            _pickPopup.pickingMode = PickingMode.Position;

            _pickPopupScrollView = new ScrollView();
            _pickPopupScrollView.AddToClassList("hierarchy-pick-popup-scroll");
            _pickPopupScrollView.style.maxHeight = 380;
            _pickPopupScrollView.pickingMode = PickingMode.Position;
            _pickPopup.Add(_pickPopupScrollView);
        }

        private void HidePickPopup()
        {
            if (_pickPopup != null)
            {
                _pickPopup.style.display = DisplayStyle.None;
                if (_pickPopup.parent != null)
                {
                    _pickPopup.RemoveFromHierarchy();
                }
            }
        }

        private void ShowPickPopup(List<PickTarget> targets, Vector2 panelPos)
        {
            if (_pickPopup == null)
            {
                CreatePickPopup();
            }

            var topRoot = DevSuiteUtils.GetTopRoot(this) ?? this;
            if (_pickPopup.parent != topRoot)
            {
                _pickPopup.RemoveFromHierarchy();
                topRoot.Add(_pickPopup);
            }

            _pickPopupScrollView.Clear();

            foreach (var target in targets)
            {
                var go = target.GameObject;
                if (go == null) continue;

                var row = new Button(() => SelectPickedObject(go));
                row.AddToClassList("hierarchy-pick-popup-row");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.FlexStart;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                row.style.marginTop = 0;
                row.style.marginBottom = 1;
                row.style.marginLeft = 0;
                row.style.marginRight = 0;
                row.style.height = 22;
                row.style.minHeight = 22;
                row.style.maxHeight = 22;
                row.style.backgroundColor = Color.clear;
                row.style.borderLeftWidth = 0;
                row.style.borderRightWidth = 0;
                row.style.borderTopWidth = 0;
                row.style.borderBottomWidth = 0;
                row.style.borderTopLeftRadius = 3;
                row.style.borderTopRightRadius = 3;
                row.style.borderBottomLeftRadius = 3;
                row.style.borderBottomRightRadius = 3;

                row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f));
                row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = Color.clear);

                var nameLabel = new Label(go.name);
                nameLabel.AddToClassList("hierarchy-pick-popup-name");
                nameLabel.style.flexGrow = 1;
                nameLabel.style.fontSize = 11;
                nameLabel.style.color = new Color(220 / 255f, 220 / 255f, 220 / 255f, 1f);
                nameLabel.style.overflow = Overflow.Hidden;
                nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                nameLabel.style.paddingLeft = 0;
                nameLabel.style.paddingRight = 0;
                nameLabel.style.paddingTop = 0;
                nameLabel.style.paddingBottom = 0;
                nameLabel.style.marginLeft = 0;
                nameLabel.style.marginRight = 0;
                nameLabel.pickingMode = PickingMode.Ignore;
                row.Add(nameLabel);

                var badgeLabel = new Label(target.Kind);
                badgeLabel.AddToClassList("hierarchy-pick-popup-badge");
                badgeLabel.style.fontSize = 9;
                badgeLabel.style.color = new Color(150 / 255f, 150 / 255f, 150 / 255f, 1f);
                badgeLabel.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
                badgeLabel.style.borderTopLeftRadius = 3;
                badgeLabel.style.borderTopRightRadius = 3;
                badgeLabel.style.borderBottomLeftRadius = 3;
                badgeLabel.style.borderBottomRightRadius = 3;
                badgeLabel.style.paddingLeft = 4;
                badgeLabel.style.paddingRight = 4;
                badgeLabel.style.paddingTop = 1;
                badgeLabel.style.paddingBottom = 1;
                badgeLabel.style.marginLeft = 6;
                badgeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                badgeLabel.pickingMode = PickingMode.Ignore;
                row.Add(badgeLabel);

                _pickPopupScrollView.Add(row);
            }

            _pickPopup.style.display = DisplayStyle.Flex;
            _pickPopup.style.visibility = Visibility.Visible;
            _pickPopup.BringToFront();

            PositionPickPopup(_pickPopup, panelPos);
        }

        private void PositionPickPopup(VisualElement popup, Vector2 panelPos)
        {
            if (popup == null || popup.parent == null) return;
            var topRoot = popup.parent;

            var rootWidth = topRoot.layout.width;
            if (float.IsNaN(rootWidth) || rootWidth <= 0) rootWidth = topRoot.resolvedStyle.width;
            if (float.IsNaN(rootWidth) || rootWidth <= 0) rootWidth = Screen.width > 0 ? Screen.width : 800f;

            var rootHeight = topRoot.layout.height;
            if (float.IsNaN(rootHeight) || rootHeight <= 0) rootHeight = topRoot.resolvedStyle.height;
            if (float.IsNaN(rootHeight) || rootHeight <= 0) rootHeight = Screen.height > 0 ? Screen.height : 600f;

            var popupWidth = popup.layout.width;
            if (float.IsNaN(popupWidth) || popupWidth <= 0) popupWidth = popup.resolvedStyle.width;
            if (float.IsNaN(popupWidth) || popupWidth <= 0) popupWidth = 220f;

            var popupHeight = popup.layout.height;
            if (float.IsNaN(popupHeight) || popupHeight <= 0) popupHeight = popup.resolvedStyle.height;
            if (float.IsNaN(popupHeight) || popupHeight <= 0) popupHeight = 225f;

            var mouseInTopRoot = topRoot.WorldToLocal(panelPos);

            var targetX = mouseInTopRoot.x + 8f;
            if (targetX + popupWidth > rootWidth - 4f)
            {
                targetX = mouseInTopRoot.x - popupWidth - 8f;
            }
            targetX = Mathf.Clamp(targetX, 4f, Mathf.Max(4f, rootWidth - popupWidth - 4f));

            var targetY = mouseInTopRoot.y + 8f;
            if (targetY + popupHeight > rootHeight - 4f)
            {
                targetY = mouseInTopRoot.y - popupHeight - 8f;
            }
            targetY = Mathf.Clamp(targetY, 4f, Mathf.Max(4f, rootHeight - popupHeight - 4f));

            popup.style.left = targetX;
            popup.style.top = targetY;
        }

        private void SelectPickedObject(GameObject pickedObj)
        {
            if (pickedObj != null)
            {
                _context.SelectedGameObject = pickedObj;
                ExpandParents(pickedObj);
                RebuildTree();

                if (_gameObjectRows.TryGetValue(pickedObj.GetInstanceID(), out var row))
                {
                    _scrollView.ScrollTo(row);
                }
            }

            HidePickPopup();
            SetPickMode(false);
        }

        private List<PickTarget> CollectPickTargets(Vector2 mousePos)
        {
            var targets = new List<PickTarget>();
            var addedIds = new HashSet<int>();

            void AddTarget(GameObject go, string kind)
            {
                if (go == null) return;
                int id = go.GetInstanceID();
                if (addedIds.Add(id))
                {
                    targets.Add(new PickTarget { GameObject = go, Kind = kind });
                }
            }

            // 1. UI Toolkit UIDocuments
            var uiDocs = Object.FindObjectsOfType<UIDocument>();
            foreach (var doc in uiDocs)
            {
                if (doc == null || !doc.gameObject.activeInHierarchy || IsGameObjectInDevSuite(doc.gameObject))
                {
                    continue;
                }

                var root = doc.rootVisualElement;
                if (root != null && root.panel != null)
                {
                    var localPos = RuntimePanelUtils.ScreenToPanel(root.panel, mousePos);
                    var picked = root.panel.Pick(localPos);
                    if (picked != null)
                    {
                        AddTarget(doc.gameObject, "UI Toolkit");
                    }
                }
            }

            // 2. Canvas UI objects (uGUI RectTransforms)
            var graphics = Object.FindObjectsOfType<UnityEngine.UI.Graphic>();
            var matchingGraphics = new List<(UnityEngine.UI.Graphic graphic, int depth, float area)>();

            foreach (var graphic in graphics)
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy || !graphic.raycastTarget)
                {
                    continue;
                }
                if (IsGameObjectInDevSuite(graphic.gameObject))
                {
                    continue;
                }

                var rect = graphic.rectTransform;
                var canvas = graphic.canvas;
                Camera eventCamera = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    eventCamera = canvas.worldCamera ?? Camera.main;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, eventCamera))
                {
                    var depth = 0;
                    var t = rect.parent;
                    while (t != null)
                    {
                        depth++;
                        t = t.parent;
                    }

                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    var area = Vector3.Distance(corners[0], corners[1]) * Vector3.Distance(corners[1], corners[2]);
                    matchingGraphics.Add((graphic, depth, area));
                }
            }

            matchingGraphics.Sort((a, b) =>
            {
                int d = b.depth.CompareTo(a.depth);
                if (d != 0) return d;
                return a.area.CompareTo(b.area);
            });

            foreach (var item in matchingGraphics)
            {
                AddTarget(item.graphic.gameObject, "UI");
            }

            // 3. Physics (3D and 2D) across cameras
            var cameras = Camera.allCameras;
            if (cameras == null || cameras.Length == 0)
            {
                var main = Camera.main;
                if (main != null) cameras = new[] { main };
            }

            if (cameras != null)
            {
                foreach (var cam in cameras)
                {
                    if (cam == null || !cam.gameObject.activeInHierarchy || !cam.enabled) continue;

                    var ray = cam.ScreenPointToRay(mousePos);

                    var hits3d = Physics.RaycastAll(ray);
                    if (hits3d != null && hits3d.Length > 0)
                    {
                        Array.Sort(hits3d, (a, b) => a.distance.CompareTo(b.distance));
                        foreach (var hit in hits3d)
                        {
                            if (hit.collider != null && !IsGameObjectInDevSuite(hit.collider.gameObject))
                            {
                                AddTarget(hit.collider.gameObject, "3D");
                            }
                        }
                    }

                    var hits2d = Physics2D.GetRayIntersectionAll(ray);
                    if (hits2d != null && hits2d.Length > 0)
                    {
                        Array.Sort(hits2d, (a, b) => a.distance.CompareTo(b.distance));
                        foreach (var hit2d in hits2d)
                        {
                            if (hit2d.collider != null && !IsGameObjectInDevSuite(hit2d.collider.gameObject))
                            {
                                AddTarget(hit2d.collider.gameObject, "2D");
                            }
                        }
                    }
                }
            }

            return targets;
        }
    }
}