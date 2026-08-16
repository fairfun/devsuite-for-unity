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

        private HashSet<string> CollapsedSceneNames => _context.HierarchyCollapsedScenes;
        private HashSet<int> ExpandedGameObjectInstanceIds => _context.HierarchyExpandedGameObjects;
        private GameObject SelectionAnchor
        {
            get => _context.HierarchySelectionAnchor;
            set => _context.HierarchySelectionAnchor = value;
        }

        private readonly HashSet<int> _matchingInstanceIds = new();
        private readonly HashSet<int> _descendantMatchingInstanceIds = new();
        private readonly Dictionary<int, VisualElement> _gameObjectRows = new();
        private readonly Dictionary<int, (GameObject Go, Toggle Toggle)> _gameObjectActivityToggles = new();
        private readonly List<VisualElement> _currentlySelectedRows = new();

        private Regex _searchRegex;
        private VisualElement _pickOverlay;
        private StyleSheet _uss;

        public HierarchyPanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            _uss = uss;
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            AddToClassList("ff-panel");
            RegisterCallback<DetachFromPanelEvent>(_ => HidePickOverlay());

            var root = this.Q<VisualElement>("hierarchy-panel-root") ?? this;

            _pickBtn = root.Q<Button>("pickBtn");
            _pickBtn.text = "\uf05b"; // crosshairs
            _pickBtn.clicked += TogglePickMode;

            _refreshBtn = root.Q<Button>("refreshBtn");
            _refreshBtn.text = "\uf021"; // sync
            _refreshBtn.clicked += () =>
            {
                _context.NotifyHierarchyChanged();
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
            _filterField.RegisterCallback<FocusOutEvent>(evt => _filterField.SetValueWithoutNotify(_context.HierarchyPattern));

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
                _context.HierarchySearchRegex = _searchByRegex;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _nameBtn = root.Q<Button>("nameBtn");
            _nameBtn.text = "\uf02b"; // tag
            _nameBtn.clicked += () =>
            {
                _searchByName = !_searchByName;
                _context.HierarchySearchByName = _searchByName;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _typeBtn = root.Q<Button>("typeBtn");
            _typeBtn.text = "\uf1b2"; // cube
            _typeBtn.clicked += () =>
            {
                _searchByType = !_searchByType;
                _context.HierarchySearchByType = _searchByType;
                UpdateButtonStates();
                HandleSearchOptionsChanged();
            };

            _dimBtn = root.Q<Button>("dimBtn");
            _dimBtn.text = "\uf042"; // adjust
            _dimBtn.clicked += () =>
            {
                _keepDimmed = !_keepDimmed;
                _context.HierarchyKeepDimmed = _keepDimmed;
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
                        _context.SelectedGameObject = UnityEditor.Selection.activeGameObject;
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
                _context.OnPickModeChanged -= HandlePickModeChanged;
                _context.OnHierarchyChanged -= HandleHierarchyChanged;
            }

            _context = context;

            _context.OnChanged += HandleContextChanged;
            _context.OnEveryFrame += HandleOnEveryFrame;
            _context.OnPickModeChanged += HandlePickModeChanged;
            _context.OnHierarchyChanged += HandleHierarchyChanged;

            _pickBtn.EnableInClassList("active", _context.PickModeActive);
            if (_context.PickModeActive)
            {
                ShowPickOverlay();
            }

            _filterField.SetValueWithoutNotify(_context.HierarchyPattern);
            _searchByRegex = _context.HierarchySearchRegex;
            _searchByName = _context.HierarchySearchByName;
            _searchByType = _context.HierarchySearchByType;
            _keepDimmed = _context.HierarchyKeepDimmed;
            UpdateButtonStates();
            UpdateSearchRegex(_filterField.value);
            PrecomputeSearch();
            RebuildTree();
        }

        public void Reset()
        {
            HidePickOverlay();

            if (_context != null)
            {
                _context.OnChanged -= HandleContextChanged;
                _context.OnEveryFrame -= HandleOnEveryFrame;
                _context.OnPickModeChanged -= HandlePickModeChanged;
                _context.OnHierarchyChanged -= HandleHierarchyChanged;
            }
        }

        private void HandlePickModeChanged(bool active)
        {
            _pickBtn.EnableInClassList("active", active);
            if (active)
            {
                ShowPickOverlay();
            }
            else
            {
                HidePickOverlay();
            }
        }

        private static Texture2D _checkerTexture;

        private static Texture2D GetOrCreateCheckerTexture()
        {
            if (_checkerTexture != null)
            {
                return _checkerTexture;
            }

            const int tileSize = 12;
            const int textureSize = tileSize * 2; // 24x24
            _checkerTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "HierarchyPickCheckerboard",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };

            var colorBlack = new Color(0f, 0f, 0f, 0.06f);
            var colorWhite = new Color(1f, 1f, 1f, 0.06f);
            var pixels = new Color[textureSize * textureSize];

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    bool isBlack = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                    pixels[y * textureSize + x] = isBlack ? colorBlack : colorWhite;
                }
            }

            _checkerTexture.SetPixels(pixels);
            _checkerTexture.Apply();
            return _checkerTexture;
        }

        private void ShowPickOverlay()
        {
            if (_pickOverlay == null)
            {
                _pickOverlay = new VisualElement();
                _pickOverlay.name = "hierarchy-pick-overlay";
                if (_uss != null)
                {
                    _pickOverlay.styleSheets.Add(_uss);
                }
                _pickOverlay.AddToClassList("hierarchy-pick-overlay");
                _pickOverlay.style.backgroundImage = new StyleBackground(GetOrCreateCheckerTexture());
                _pickOverlay.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                _pickOverlay.style.backgroundSize = new BackgroundSize(new Length(24, LengthUnit.Pixel), new Length(24, LengthUnit.Pixel));
                _pickOverlay.pickingMode = PickingMode.Position;

                _pickOverlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
                _pickOverlay.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
                _pickOverlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
                _pickOverlay.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
                _pickOverlay.RegisterCallback<MouseUpEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
            }

            var topRoot = DevSuiteUtils.GetTopRoot(this) ?? this;
            if (_pickOverlay.parent != topRoot)
            {
                _pickOverlay.RemoveFromHierarchy();
                topRoot.Insert(0, _pickOverlay);
            }

            _pickOverlay.style.display = DisplayStyle.Flex;
            _pickOverlay.SendToBack();
        }

        private void HidePickOverlay()
        {
            if (_pickOverlay != null)
            {
                _pickOverlay.style.display = DisplayStyle.None;
                if (_pickOverlay.parent != null)
                {
                    _pickOverlay.RemoveFromHierarchy();
                }
            }
        }

        private void HandleContextChanged()
        {
            _pickBtn.EnableInClassList("active", _context.PickModeActive);

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

            if (_context.SelectedGameObject != null)
            {
                var targetId = _context.SelectedGameObject.GetInstanceID();
                if (!_gameObjectRows.ContainsKey(targetId))
                {
                    ExpandParents(_context.SelectedGameObject);
                    RebuildTree();
                }

                if (_gameObjectRows.TryGetValue(targetId, out var row))
                {
                    SafeScrollTo(row);
                }
            }

            UpdateSelectionHighlight();
        }

        private void SafeScrollTo(VisualElement row)
        {
            if (_scrollView == null || row == null)
            {
                return;
            }

            _scrollView.schedule.Execute(() =>
            {
                if (_scrollView != null && _scrollView.panel != null && row != null && row.panel != null)
                {
                    try
                    {
                        _scrollView.ScrollTo(row);
                    }
                    catch (Exception)
                    {
                        // Ignore UI Toolkit internal measurement edge cases
                    }
                }
            });
        }

        private void TogglePickMode()
        {
            _context.PickModeActive = !_context.PickModeActive;
        }

        private void HandleHierarchyChanged()
        {
            var focused = _filterField.focusController?.focusedElement as VisualElement;
            if (focused == null || !_filterField.Contains(focused))
            {
                _filterField.SetValueWithoutNotify(_context.HierarchyPattern);
            }

            _searchByRegex = _context.HierarchySearchRegex;
            _searchByName = _context.HierarchySearchByName;
            _searchByType = _context.HierarchySearchByType;
            _keepDimmed = _context.HierarchyKeepDimmed;
            UpdateButtonStates();
            UpdateSearchRegex(_filterField.value);
            PrecomputeSearch();
            RebuildTree();
        }

        private void HandleSearchChanged(string query)
        {
            _context.HierarchyPattern = query;
        }

        private void HandleSearchOptionsChanged()
        {
            _context.NotifyHierarchyChanged();
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

        private static readonly List<Component> _componentCache = new();
        private static readonly List<string> _typeNamesCache = new();

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
                _componentCache.Clear();
                go.GetComponents(typeof(Component), _componentCache);
                for (var i = 0; i < _componentCache.Count; i++)
                {
                    var comp = _componentCache[i];
                    if (comp != null && regex.IsMatch(comp.GetType().Name))
                    {
                        _componentCache.Clear();
                        return true;
                    }
                }

                _componentCache.Clear();
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

        private void ToggleSceneCollapsed(string sceneName)
        {
            if (!CollapsedSceneNames.Contains(sceneName))
            {
                CollapsedSceneNames.Add(sceneName);
            }
            else
            {
                CollapsedSceneNames.Remove(sceneName);
            }

            _context.NotifyHierarchyChanged();
        }

        private void ToggleGameObjectExpanded(int instanceId)
        {
            if (ExpandedGameObjectInstanceIds.Contains(instanceId))
            {
                ExpandedGameObjectInstanceIds.Remove(instanceId);
            }
            else
            {
                ExpandedGameObjectInstanceIds.Add(instanceId);
            }

            _context.NotifyHierarchyChanged();
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

            var isExpanded = !CollapsedSceneNames.Contains(sceneName);
            foldoutBtn.text = isExpanded ? "\uf0d7" : "\uf0da";
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
                        ToggleSceneCollapsed(sceneName);
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
                ToggleSceneCollapsed(sceneName);
            };

            if (isExpanded)
            {
                var rootObjects = scene.GetRootGameObjects();
                foreach (var go in rootObjects)
                {
                    RenderGameObjectNode(go, 1, childrenContainer);
                }
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
            var isExpanded = ExpandedGameObjectInstanceIds.Contains(instanceId) || (_searchRegex != null && hasMatchingDescendant);

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

            var badgeKind = GetGameObjectKind(go);
            if (!string.IsNullOrEmpty(badgeKind))
            {
                var badgeLabel = new Label(badgeKind);
                badgeLabel.AddToClassList("hierarchy-badge");
                badgeLabel.AddToClassList(GetBadgeClassForKind(badgeKind));
                badgeLabel.pickingMode = PickingMode.Ignore;
                row.Add(badgeLabel);
            }

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
                ToggleGameObjectExpanded(instanceId);
            };

            row.RegisterCallback<ClickEvent>(
                evt =>
                {
                    if (evt.clickCount == 2 && hasChildren)
                    {
                        ToggleGameObjectExpanded(instanceId);
                        evt.StopPropagation();
                    }
                    else if (evt.clickCount == 1)
                    {
                        var isCtrlHeld = evt.ctrlKey || evt.commandKey;
                        var isShiftHeld = evt.shiftKey;

                        if (isShiftHeld && SelectionAnchor != null)
                        {
                            var visibleList = GetVisibleGameObjectsInOrder();
                            if (visibleList.Contains(SelectionAnchor) && visibleList.Contains(go))
                            {
                                var anchorIndex = visibleList.IndexOf(SelectionAnchor);
                                var targetIndex = visibleList.IndexOf(go);
                                var start = Mathf.Min(anchorIndex, targetIndex);
                                var end = Mathf.Max(anchorIndex, targetIndex);

                                var range = new List<GameObject>();
                                for (var i = start; i <= end; i++)
                                {
                                    range.Add(visibleList[i]);
                                }

                                _context.SetSelectedGameObjects(range);
                                _context.InspectorVisible = true;
#if UNITY_EDITOR
                                UnityEditor.Selection.objects = range.ToArray();
#endif
                            }
                        }
                        else
                        {
                            SelectionAnchor = go;
                            if (isCtrlHeld)
                            {
                                _context.ToggleSelectedGameObject(go);
                            }
                            else
                            {
                                _context.SelectedGameObject = go;
                            }

                            _context.InspectorVisible = true;
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
            );

            if (hasChildren && isExpanded)
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

            if (SelectionAnchor == null || !_context.SelectedGameObjects.Contains(SelectionAnchor))
            {
                SelectionAnchor = _context.SelectedGameObject;
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
                if (CollapsedSceneNames.Contains(scene.name))
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
            if (ExpandedGameObjectInstanceIds.Contains(instanceId))
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
            _context.InspectorVisible = true;
            ExpandParents(target);
            _context.NotifyHierarchyChanged();

            if (_gameObjectRows.TryGetValue(target.GetInstanceID(), out var row))
            {
                SafeScrollTo(row);
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

            CollapsedSceneNames.Remove(go.scene.name);

            var parent = go.transform.parent;
            while (parent != null)
            {
                ExpandedGameObjectInstanceIds.Add(parent.gameObject.GetInstanceID());
                parent = parent.parent;
            }
        }

        private void HandleOnEveryFrame()
        {
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

        private void UpdateButtonStates()
        {
            _regexBtn.EnableInClassList("active", _searchByRegex);
            _nameBtn.EnableInClassList("active", _searchByName);
            _typeBtn.EnableInClassList("active", _searchByType);
            _dimBtn.EnableInClassList("active", _keepDimmed);
        }

        private GameObject GetSelectedGameObject()
        {
            if (_context.SelectedGameObject != null)
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

            var indent = new string(' ', depth * 4);
            _componentCache.Clear();
            _typeNamesCache.Clear();
            go.GetComponents(typeof(Component), _componentCache);
            for (var i = 0; i < _componentCache.Count; i++)
            {
                var comp = _componentCache[i];
                if (comp == null)
                {
                    continue;
                }

                var typeName = comp.GetType().Name;
                if (!_typeNamesCache.Contains(typeName))
                {
                    _typeNamesCache.Add(typeName);
                }
            }

            var typesStr = _typeNamesCache.Count > 0 ? $" ({string.Join(", ", _typeNamesCache)})" : "";
            _componentCache.Clear();
            _typeNamesCache.Clear();
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
#endif

        private static string GetGameObjectKind(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            if (go.transform is RectTransform)
            {
                return "UI";
            }

            _componentCache.Clear();
            go.GetComponents(typeof(Component), _componentCache);

            var has2D = false;
            var has3D = false;
            var hasUI = false;

            for (var i = 0; i < _componentCache.Count; i++)
            {
                var comp = _componentCache[i];
                if (comp == null)
                {
                    continue;
                }

                if (comp is UIDocument)
                {
                    _componentCache.Clear();
                    return "UI Toolkit";
                }

                if (comp is Canvas or UnityEngine.UI.Graphic or CanvasRenderer)
                {
                    hasUI = true;
                }
                else if (comp is Collider2D or Rigidbody2D or SpriteRenderer)
                {
                    has2D = true;
                }
                else if (comp is Collider or Rigidbody or Renderer or MeshFilter or Camera or Light or Terrain or ParticleSystem)
                {
                    has3D = true;
                }
            }

            _componentCache.Clear();

            if (hasUI)
            {
                return "UI";
            }

            if (has2D)
            {
                return "2D";
            }

            if (has3D)
            {
                return "3D";
            }

            return null;
        }

        private static string GetBadgeClassForKind(string kind)
        {
            return kind switch
            {
                "UI Toolkit" => "badge-uitoolkit",
                "UI" => "badge-ugui",
                "2D" => "badge-2d",
                "3D" => "badge-3d",
                _ => "badge-default"
            };
        }
    }
}