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
        private const float RowHeight = 23f;

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
        private readonly VisualElement _topSpacer;
        private readonly VisualElement _rowsContainer;
        private readonly VisualElement _bottomSpacer;

        private readonly List<HierarchyRowView> _rowPool = new();
        private readonly List<HierarchyItem> _flatItems = new();
        private readonly HashSet<int> _selectedInstanceIdsCache = new();

        private HashSet<string> CollapsedSceneNames => _context.HierarchyCollapsedScenes;
        private HashSet<int> ExpandedGameObjectInstanceIds => _context.HierarchyExpandedGameObjects;

        private GameObject SelectionAnchor
        {
            get => _context.HierarchySelectionAnchor;
            set => _context.HierarchySelectionAnchor = value;
        }

        private readonly HashSet<int> _matchingInstanceIds = new();
        private readonly HashSet<int> _descendantMatchingInstanceIds = new();

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

            _topSpacer = new VisualElement { name = "hierarchyTopSpacer" };
            _topSpacer.style.flexShrink = 0;
            _topSpacer.style.flexGrow = 0;
            _topSpacer.pickingMode = PickingMode.Ignore;
            _scrollView.Add(_topSpacer);

            _rowsContainer = new VisualElement { name = "hierarchyRowsContainer" };
            _rowsContainer.style.flexShrink = 0;
            _rowsContainer.style.flexGrow = 0;
            _rowsContainer.style.flexDirection = FlexDirection.Column;
            _scrollView.Add(_rowsContainer);

            _bottomSpacer = new VisualElement { name = "hierarchyBottomSpacer" };
            _bottomSpacer.style.flexShrink = 0;
            _bottomSpacer.style.flexGrow = 0;
            _bottomSpacer.pickingMode = PickingMode.Ignore;
            _scrollView.Add(_bottomSpacer);

            _scrollView.verticalScroller.valueChanged += _ => UpdateVisibleRows();
            _scrollView.RegisterCallback<GeometryChangedEvent>(_ => UpdateVisibleRows());

            RegisterCallback<AttachToPanelEvent>(
                evt =>
                {
#if UNITY_EDITOR
                    UnityEditor.Selection.selectionChanged += HandleEditorSelectionChanged;
                    // Sync initial selection
                    if (_context != null)
                        _context.SetSelectedGameObjectsFromEditor(UnityEditor.Selection.gameObjects);
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
            RebuildFlatList();
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

            _flatItems.Clear();
            UpdateVisibleRows();
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
                return;
            }

            if (_context.SelectedGameObject != null)
            {
                var targetGo = _context.SelectedGameObject;
                bool parentsExpanded = EnsureParentsExpanded(targetGo);
                if (parentsExpanded)
                {
                    RebuildFlatList();
                }

                int targetIndex = FindGameObjectIndex(targetGo);
                if (targetIndex >= 0)
                {
                    SafeScrollToIndex(targetIndex);
                }
            }

            UpdateSelectionHighlight();
        }

        public void SafeScrollTo(VisualElement row)
        {
            if (row != null && _scrollView != null && _scrollView.panel != null)
            {
                _scrollView.schedule.Execute(() =>
                {
                    try
                    {
                        _scrollView.ScrollTo(row);
                    }
                    catch (Exception)
                    {
                        // Ignore UI Toolkit internal measurement edge cases
                    }
                });
            }
        }

        public void SafeScrollTo(GameObject go)
        {
            int index = FindGameObjectIndex(go);
            if (index >= 0)
            {
                SafeScrollToIndex(index);
            }
        }

        private void SafeScrollToIndex(int targetIndex)
        {
            if (_scrollView == null || targetIndex < 0 || targetIndex >= _flatItems.Count)
            {
                return;
            }

            _scrollView.schedule.Execute(() =>
            {
                if (_scrollView == null || _scrollView.panel == null) return;

                float viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
                if (float.IsNaN(viewportHeight) || viewportHeight <= 0)
                {
                    viewportHeight = _scrollView.resolvedStyle.height;
                }
                if (float.IsNaN(viewportHeight) || viewportHeight <= 0)
                {
                    viewportHeight = 400f;
                }

                float targetY = targetIndex * RowHeight;
                float currentScrollY = _scrollView.scrollOffset.y;

                if (targetY < currentScrollY)
                {
                    _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, targetY);
                }
                else if (targetY + RowHeight > currentScrollY + viewportHeight)
                {
                    _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, targetY + RowHeight - viewportHeight);
                }

                UpdateVisibleRows();
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
            RebuildFlatList();
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
            RebuildFlatList();
        }

        private void RebuildFlatList()
        {
            _flatItems.Clear();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                FlattenSceneNode(scene);
            }

            UpdateVisibleRows();
            UpdateSelectionHighlight();
        }

        private void FlattenSceneNode(Scene scene)
        {
            var sceneName = scene.name;
            var isExpanded = !CollapsedSceneNames.Contains(sceneName);

            var sceneItem = new HierarchyItem
            {
                Type = HierarchyItemType.Scene,
                Scene = scene,
                SceneName = sceneName,
                Depth = 0,
                HasChildren = true,
                IsExpanded = isExpanded,
                IsMatching = true,
                HasMatchingDescendant = false
            };

            _flatItems.Add(sceneItem);

            if (isExpanded)
            {
                var rootObjects = scene.GetRootGameObjects();
                for (var i = 0; i < rootObjects.Length; i++)
                {
                    FlattenGameObjectNode(rootObjects[i], 1);
                }
            }
        }

        private void FlattenGameObjectNode(GameObject go, int depth)
        {
            if (go == null) return;

            var instanceId = go.GetInstanceID();
            var isMatching = _searchRegex == null || _matchingInstanceIds.Contains(instanceId);
            var hasMatchingDescendant = _searchRegex == null || _descendantMatchingInstanceIds.Contains(instanceId);

            if (_searchRegex != null && !_keepDimmed && !isMatching && !hasMatchingDescendant)
            {
                return;
            }

            var transform = go.transform;
            var childCount = transform.childCount;
            var hasChildren = childCount > 0;
            var isExpanded = ExpandedGameObjectInstanceIds.Contains(instanceId) || (_searchRegex != null && hasMatchingDescendant);

            var item = new HierarchyItem
            {
                Type = HierarchyItemType.GameObject,
                GameObject = go,
                InstanceId = instanceId,
                Depth = depth,
                HasChildren = hasChildren,
                IsExpanded = isExpanded,
                IsMatching = isMatching,
                HasMatchingDescendant = hasMatchingDescendant,
                BadgeKind = null // Lazily evaluated on bind
            };

            _flatItems.Add(item);

            if (hasChildren && isExpanded)
            {
                for (var i = 0; i < childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (child != null)
                    {
                        FlattenGameObjectNode(child.gameObject, depth + 1);
                    }
                }
            }
        }

        private void UpdateVisibleRows()
        {
            if (_scrollView == null || _flatItems == null) return;

            int totalCount = _flatItems.Count;
            if (totalCount == 0)
            {
                _topSpacer.style.height = 0;
                _bottomSpacer.style.height = 0;
                for (var i = 0; i < _rowPool.Count; i++)
                {
                    _rowPool[i].style.display = DisplayStyle.None;
                    _rowPool[i].Unbind();
                }
                return;
            }

            float viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
            if (float.IsNaN(viewportHeight) || viewportHeight <= 0)
            {
                viewportHeight = _scrollView.resolvedStyle.height;
            }
            if (float.IsNaN(viewportHeight) || viewportHeight <= 0)
            {
                viewportHeight = 400f;
            }

            float maxScrollY = Mathf.Max(0f, (totalCount * RowHeight) - viewportHeight);
            if (_scrollView.scrollOffset.y > maxScrollY)
            {
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, maxScrollY);
            }

            const int buffer = 3;
            float scrollY = Mathf.Max(0f, _scrollView.scrollOffset.y);
            int firstVisibleIndex = Mathf.Clamp(Mathf.FloorToInt(scrollY / RowHeight) - buffer, 0, totalCount - 1);
            int visibleCount = Mathf.CeilToInt(viewportHeight / RowHeight) + (buffer * 2);
            int lastVisibleIndex = Mathf.Clamp(firstVisibleIndex + visibleCount - 1, 0, totalCount - 1);

            int countToDisplay = lastVisibleIndex - firstVisibleIndex + 1;

            while (_rowPool.Count < countToDisplay)
            {
                var row = new HierarchyRowView(this);
                _rowPool.Add(row);
                _rowsContainer.Add(row);
            }

            float topHeight = firstVisibleIndex * RowHeight;
            float bottomHeight = Mathf.Max(0f, (totalCount - 1 - lastVisibleIndex) * RowHeight);

            _topSpacer.style.height = topHeight;
            _bottomSpacer.style.height = bottomHeight;

            bool hasSearch = _searchRegex != null;
            bool keepDimmed = _keepDimmed;
            UpdateSelectedInstanceIdsCache();

            for (var i = 0; i < _rowPool.Count; i++)
            {
                var row = _rowPool[i];
                if (i < countToDisplay)
                {
                    int itemIndex = firstVisibleIndex + i;
                    row.style.display = DisplayStyle.Flex;
                    row.Bind(_flatItems[itemIndex], _selectedInstanceIdsCache, hasSearch, keepDimmed);
                }
                else
                {
                    row.style.display = DisplayStyle.None;
                    row.Unbind();
                }
            }
        }

        private void ToggleItemExpanded(HierarchyItem item)
        {
            if (item == null) return;

            if (item.Type == HierarchyItemType.Scene)
            {
                ToggleSceneCollapsed(item.SceneName);
            }
            else if (item.Type == HierarchyItemType.GameObject)
            {
                ToggleGameObjectExpanded(item.InstanceId);
            }
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

        private void HandleGameObjectClicked(GameObject go, bool isCtrlHeld, bool isShiftHeld)
        {
            if (go == null) return;

            if (isShiftHeld && SelectionAnchor != null)
            {
                int anchorIndex = FindGameObjectIndex(SelectionAnchor);
                int targetIndex = FindGameObjectIndex(go);

                if (anchorIndex >= 0 && targetIndex >= 0)
                {
                    var start = Mathf.Min(anchorIndex, targetIndex);
                    var end = Mathf.Max(anchorIndex, targetIndex);

                    var range = new List<GameObject>();
                    for (var i = start; i <= end; i++)
                    {
                        var item = _flatItems[i];
                        if (item.Type == HierarchyItemType.GameObject && item.GameObject != null)
                        {
                            range.Add(item.GameObject);
                        }
                    }

                    _context.SetSelectedGameObjects(range);
                    _context.InspectorVisible = true;
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

        private int FindGameObjectIndex(GameObject go)
        {
            if (go == null) return -1;
            for (var i = 0; i < _flatItems.Count; i++)
            {
                var item = _flatItems[i];
                if (item.Type == HierarchyItemType.GameObject && item.GameObject == go)
                {
                    return i;
                }
            }
            return -1;
        }

        private bool EnsureParentsExpanded(GameObject go)
        {
            if (go == null) return false;
            bool changed = false;

            if (CollapsedSceneNames.Contains(go.scene.name))
            {
                CollapsedSceneNames.Remove(go.scene.name);
                changed = true;
            }

            var parent = go.transform.parent;
            while (parent != null)
            {
                var parentGo = parent.gameObject;
                int parentId = parentGo.GetInstanceID();
                if (!ExpandedGameObjectInstanceIds.Contains(parentId))
                {
                    ExpandedGameObjectInstanceIds.Add(parentId);
                    changed = true;
                }
                parent = parent.parent;
            }

            return changed;
        }

        private void ExpandParents(GameObject go)
        {
            EnsureParentsExpanded(go);
        }

        private void UpdateSelectedInstanceIdsCache()
        {
            _selectedInstanceIdsCache.Clear();
            if (_context?.SelectedGameObjects != null)
            {
                var list = _context.SelectedGameObjects;
                for (var i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go != null)
                    {
                        _selectedInstanceIdsCache.Add(go.GetInstanceID());
                    }
                }
            }
        }

        private void UpdateSelectionHighlight()
        {
            UpdateSelectedInstanceIdsCache();

            for (var i = 0; i < _rowPool.Count; i++)
            {
                var row = _rowPool[i];
                if (row.style.display != DisplayStyle.None)
                {
                    row.UpdateSelection(_selectedInstanceIdsCache);
                }
            }

            if (SelectionAnchor == null || !_selectedInstanceIdsCache.Contains(SelectionAnchor.GetInstanceID()))
            {
                SelectionAnchor = _context.SelectedGameObject;
            }
        }

        private List<GameObject> GetVisibleGameObjectsInOrder()
        {
            var visibleList = new List<GameObject>(_flatItems.Count);
            for (var i = 0; i < _flatItems.Count; i++)
            {
                var item = _flatItems[i];
                if (item.Type == HierarchyItemType.GameObject && item.GameObject != null)
                {
                    visibleList.Add(item.GameObject);
                }
            }
            return visibleList;
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
            EnsureParentsExpanded(target);
            _context.SelectedGameObject = target;
            _context.InspectorVisible = true;
            _context.NotifyHierarchyChanged();
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

        private void HandleOnEveryFrame()
        {
            SyncActivityStates();
        }

        private void SyncActivityStates()
        {
            for (var i = 0; i < _rowPool.Count; i++)
            {
                var row = _rowPool[i];
                if (row.style.display != DisplayStyle.None)
                {
                    row.SyncActivityState();
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
            if (_context != null && _context.IsSyncingEditorSelection)
            {
                return;
            }

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
                _context.SetSelectedGameObjectsFromEditor(newSelection);
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

        private enum HierarchyItemType
        {
            Scene,
            GameObject
        }

        private class HierarchyItem
        {
            public HierarchyItemType Type;
            public Scene Scene;
            public string SceneName;
            public GameObject GameObject;
            public int InstanceId;
            public int Depth;
            public bool HasChildren;
            public bool IsExpanded;
            public bool IsMatching;
            public bool HasMatchingDescendant;
            public string BadgeKind;
        }

        private class HierarchyRowView : VisualElement
        {
            private readonly HierarchyPanelView _owner;
            private readonly Button _foldoutBtn;
            private readonly Label _itemLabel;
            private readonly Label _badgeLabel;
            private readonly Toggle _activityToggle;
            private HierarchyItem _item;
            private bool _isBinding;

            public HierarchyItem Item => _item;

            public HierarchyRowView(HierarchyPanelView owner)
            {
                _owner = owner;
                AddToClassList("hierarchy-item-row");
                style.flexShrink = 0;
                style.flexGrow = 0;

                _foldoutBtn = new Button { name = "foldoutBtn" };
                _foldoutBtn.AddToClassList("hierarchy-foldout-btn");
                _foldoutBtn.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                _foldoutBtn.clicked += OnFoldoutClicked;
                Add(_foldoutBtn);

                _itemLabel = new Label { name = "itemLabel" };
                _itemLabel.AddToClassList("hierarchy-item-label");
                Add(_itemLabel);

                _badgeLabel = new Label { name = "badgeLabel" };
                _badgeLabel.AddToClassList("hierarchy-badge");
                _badgeLabel.pickingMode = PickingMode.Ignore;
                _badgeLabel.style.display = DisplayStyle.None;
                Add(_badgeLabel);

                _activityToggle = new Toggle
                {
                    name = "activityToggle",
                    tooltip = "Toggle active state"
                };
                _activityToggle.AddToClassList("ff-toggle");
                _activityToggle.AddToClassList("hierarchy-activity-toggle");

                var checkmark = _activityToggle.Q<VisualElement>("unity-checkmark");
                if (checkmark != null)
                {
                    var icon = new Label("\uf00c");
                    icon.AddToClassList("ff-toggle-icon");
                    checkmark.Add(icon);
                }
                else
                {
                    _activityToggle.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var cm = _activityToggle.Q<VisualElement>("unity-checkmark");
                        if (cm != null && cm.childCount == 0)
                        {
                            var icon = new Label("\uf00c");
                            icon.AddToClassList("ff-toggle-icon");
                            cm.Add(icon);
                        }
                    });
                }

                _activityToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                _activityToggle.RegisterValueChangedCallback(OnActivityToggleChanged);
                Add(_activityToggle);

                RegisterCallback<ClickEvent>(OnRowClicked);
            }

            private void OnFoldoutClicked()
            {
                if (_item == null) return;
                _owner.ToggleItemExpanded(_item);
            }

            private void OnRowClicked(ClickEvent evt)
            {
                if (_item == null) return;

                if (evt.clickCount == 2)
                {
                    if (_item.HasChildren || _item.Type == HierarchyItemType.Scene)
                    {
                        _owner.ToggleItemExpanded(_item);
                        evt.StopPropagation();
                    }
                }
                else if (evt.clickCount == 1)
                {
                    if (_item.Type == HierarchyItemType.GameObject)
                    {
                        var isCtrlHeld = evt.ctrlKey || evt.commandKey;
                        var isShiftHeld = evt.shiftKey;
                        _owner.HandleGameObjectClicked(_item.GameObject, isCtrlHeld, isShiftHeld);
                    }
                }
            }

            private void OnActivityToggleChanged(ChangeEvent<bool> evt)
            {
                if (_isBinding) return;
                if (_item?.GameObject != null)
                {
                    _item.GameObject.SetActive(evt.newValue);
                    EnableInClassList("inactive", !evt.newValue);
                }
            }

            public void Bind(HierarchyItem item, HashSet<int> selectedInstanceIds, bool hasSearch, bool keepDimmed)
            {
                _isBinding = true;
                _item = item;

                style.paddingLeft = 6 + (item.Depth * 16);

                if (item.Type == HierarchyItemType.Scene)
                {
                    RemoveFromClassList("hierarchy-object-row");
                    RemoveFromClassList("inactive");
                    RemoveFromClassList("dimmed");
                    RemoveFromClassList("selected");
                    AddToClassList("hierarchy-scene-row");

                    _itemLabel.text = $"Scene: {item.SceneName}";
                    _foldoutBtn.style.visibility = Visibility.Visible;
                    _foldoutBtn.text = item.IsExpanded ? "\uf0d7" : "\uf0da";
                    _badgeLabel.style.display = DisplayStyle.None;
                    _activityToggle.style.display = DisplayStyle.None;
                }
                else
                {
                    RemoveFromClassList("hierarchy-scene-row");
                    AddToClassList("hierarchy-object-row");

                    var go = item.GameObject;
                    if (go == null)
                    {
                        _itemLabel.text = "<Destroyed>";
                        _foldoutBtn.style.visibility = Visibility.Hidden;
                        _foldoutBtn.text = "";
                        _badgeLabel.style.display = DisplayStyle.None;
                        _activityToggle.style.display = DisplayStyle.None;
                        RemoveFromClassList("inactive");
                        RemoveFromClassList("dimmed");
                        RemoveFromClassList("selected");
                    }
                    else
                    {
                        _itemLabel.text = go.name;

                        bool isActive = go.activeSelf;
                        EnableInClassList("inactive", !isActive);

                        _activityToggle.style.display = DisplayStyle.Flex;
                        _activityToggle.SetValueWithoutNotify(isActive);

                        if (item.HasChildren)
                        {
                            _foldoutBtn.style.visibility = Visibility.Visible;
                            _foldoutBtn.text = item.IsExpanded ? "\uf0d7" : "\uf0da";
                        }
                        else
                        {
                            _foldoutBtn.style.visibility = Visibility.Hidden;
                            _foldoutBtn.text = "";
                        }

                        if (hasSearch && keepDimmed)
                        {
                            bool isDimmed = !(item.IsMatching || item.HasMatchingDescendant);
                            EnableInClassList("dimmed", isDimmed);
                        }
                        else
                        {
                            RemoveFromClassList("dimmed");
                        }

                        if (item.BadgeKind == null)
                        {
                            item.BadgeKind = GetGameObjectKind(go) ?? string.Empty;
                        }

                        if (!string.IsNullOrEmpty(item.BadgeKind))
                        {
                            _badgeLabel.text = item.BadgeKind;
                            _badgeLabel.RemoveFromClassList("badge-uitoolkit");
                            _badgeLabel.RemoveFromClassList("badge-ugui");
                            _badgeLabel.RemoveFromClassList("badge-2d");
                            _badgeLabel.RemoveFromClassList("badge-3d");
                            _badgeLabel.RemoveFromClassList("badge-default");
                            _badgeLabel.AddToClassList(GetBadgeClassForKind(item.BadgeKind));
                            _badgeLabel.style.display = DisplayStyle.Flex;
                        }
                        else
                        {
                            _badgeLabel.style.display = DisplayStyle.None;
                        }

                        EnableInClassList("selected", selectedInstanceIds.Contains(item.InstanceId));
                    }
                }

                _isBinding = false;
            }

            public void Unbind()
            {
                _item = null;
            }

            public void UpdateSelection(HashSet<int> selectedInstanceIds)
            {
                if (_item != null && _item.Type == HierarchyItemType.GameObject)
                {
                    EnableInClassList("selected", selectedInstanceIds.Contains(_item.InstanceId));
                }
                else
                {
                    RemoveFromClassList("selected");
                }
            }

            public void SyncActivityState()
            {
                if (_item?.GameObject == null) return;
                var go = _item.GameObject;

                if (_itemLabel.text != go.name)
                {
                    _itemLabel.text = go.name;
                }

                bool isActive = go.activeSelf;
                if (_activityToggle.value != isActive)
                {
                    _isBinding = true;
                    _activityToggle.SetValueWithoutNotify(isActive);
                    _isBinding = false;
                    EnableInClassList("inactive", !isActive);
                }
            }
        }
    }
}