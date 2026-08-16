using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ff.DevSuite.View
{
    internal class PickSelectionPanelView : VisualElement
    {
        private DevSuiteContext _context;

        private struct PickTarget
        {
            public GameObject GameObject;
            public string Kind;
        }

        private VisualElement _pickPopup;
        private ScrollView _pickPopupScrollView;

        public PickSelectionPanelView(StyleSheet uss = null)
        {
            if (uss != null)
            {
                styleSheets.Add(uss);
            }

            AddToClassList("pick-selection-panel");
            pickingMode = PickingMode.Ignore;

            CreatePickPopup(uss);

            RegisterCallback<DetachFromPanelEvent>(
                _ =>
                {
                    if (_context != null && _context.PickModeActive)
                    {
                        _context.PickModeActive = false;
                    }
                }
            );
        }

        public void Initialize(DevSuiteContext context)
        {
            _context = context;
            if (_context != null)
            {
                _context.OnPickModeChanged += HandlePickModeChanged;
                _context.OnEveryFrame += HandleOnEveryFrame;
                _context.OnChanged += HandleContextChanged;

                if (_context.PickModeActive)
                {
                    HandlePickModeChanged(true);
                }
            }
        }

        public void Reset()
        {
            HidePickPopup();

            if (_context != null)
            {
                _context.OnPickModeChanged -= HandlePickModeChanged;
                _context.OnEveryFrame -= HandleOnEveryFrame;
                _context.OnChanged -= HandleContextChanged;
                _context = null;
            }
        }

        private void HandlePickModeChanged(bool active)
        {
            if (!active)
            {
                HidePickPopup();
            }
        }

        private void HandleContextChanged()
        {
            if (_context != null && (!_context.PanelExpanded || !_context.HierarchyVisible) && _context.PickModeActive)
            {
                _context.PickModeActive = false;
            }
        }

        private void HandleOnEveryFrame()
        {
            if (_context == null || !_context.PickModeActive)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _context.PickModeActive = false;
                return;
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _context.PickModeActive = false;
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
                var screenHeight = Screen.height > 0 ? Screen.height : 600f;
                var screenWidth = Screen.width > 0 ? Screen.width : 800f;
                var panelWidth = topRoot?.layout.width > 0 ? topRoot.layout.width : topRoot?.resolvedStyle.width > 0 ? topRoot.resolvedStyle.width : screenWidth;
                var panelHeight = topRoot?.layout.height > 0 ? topRoot.layout.height : topRoot?.resolvedStyle.height > 0 ? topRoot.resolvedStyle.height : screenHeight;

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

        private void CreatePickPopup(StyleSheet uss = null)
        {
            _pickPopup = new VisualElement();
            if (uss != null && !_pickPopup.styleSheets.Contains(uss))
            {
                _pickPopup.styleSheets.Add(uss);
            }
            _pickPopup.AddToClassList("pick-selection-popup");
            _pickPopup.style.display = DisplayStyle.None;
            _pickPopup.pickingMode = PickingMode.Position;

            _pickPopupScrollView = new ScrollView();
            _pickPopupScrollView.AddToClassList("pick-selection-popup-scroll");
            _pickPopupScrollView.pickingMode = PickingMode.Position;
            _pickPopup.Add(_pickPopupScrollView);

            Add(_pickPopup);
        }

        private void HidePickPopup()
        {
            if (_pickPopup != null)
            {
                _pickPopup.style.display = DisplayStyle.None;
            }
        }

        private void ShowPickPopup(List<PickTarget> targets, Vector2 panelPos)
        {
            if (_pickPopup == null)
            {
                CreatePickPopup();
            }

            if (_pickPopup.parent != this)
            {
                _pickPopup.RemoveFromHierarchy();
                Add(_pickPopup);
            }

            _pickPopupScrollView.Clear();

            foreach (var target in targets)
            {
                var go = target.GameObject;
                if (go == null)
                {
                    continue;
                }

                var row = new Button(() => SelectPickedObject(go));
                row.AddToClassList("pick-selection-popup-row");

                var nameLabel = new Label(go.name);
                nameLabel.AddToClassList("pick-selection-popup-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                row.Add(nameLabel);

                var badgeLabel = new Label(target.Kind);
                badgeLabel.AddToClassList("pick-selection-popup-badge");
                badgeLabel.AddToClassList(GetBadgeClassForKind(target.Kind));
                badgeLabel.pickingMode = PickingMode.Ignore;
                row.Add(badgeLabel);

                _pickPopupScrollView.Add(row);
            }

            _pickPopup.style.display = DisplayStyle.Flex;
            _pickPopup.BringToFront();

            PositionPickPopup(_pickPopup, panelPos);
        }

        private static string GetBadgeClassForKind(string kind) => kind switch
        {
            "UI Toolkit" => "badge-uitoolkit",
            "UI" => "badge-ugui",
            "2D" => "badge-2d",
            "3D" => "badge-3d",
            _ => "badge-default"
        };

        private void PositionPickPopup(VisualElement popup, Vector2 panelPos)
        {
            if (popup == null)
            {
                return;
            }
            var container = popup.parent ?? this;

            var rootWidth = container.layout.width;
            if (float.IsNaN(rootWidth) || rootWidth <= 0)
            {
                rootWidth = container.resolvedStyle.width;
            }
            if (float.IsNaN(rootWidth) || rootWidth <= 0)
            {
                rootWidth = Screen.width > 0 ? Screen.width : 800f;
            }

            var rootHeight = container.layout.height;
            if (float.IsNaN(rootHeight) || rootHeight <= 0)
            {
                rootHeight = container.resolvedStyle.height;
            }
            if (float.IsNaN(rootHeight) || rootHeight <= 0)
            {
                rootHeight = Screen.height > 0 ? Screen.height : 600f;
            }

            var popupWidth = popup.layout.width;
            if (float.IsNaN(popupWidth) || popupWidth <= 0)
            {
                popupWidth = popup.resolvedStyle.width;
            }
            if (float.IsNaN(popupWidth) || popupWidth <= 0)
            {
                popupWidth = 220f;
            }

            var popupHeight = popup.layout.height;
            if (float.IsNaN(popupHeight) || popupHeight <= 0)
            {
                popupHeight = popup.resolvedStyle.height;
            }
            if (float.IsNaN(popupHeight) || popupHeight <= 0)
            {
                popupHeight = 225f;
            }

            var mouseInContainer = container.WorldToLocal(panelPos);

            var targetX = mouseInContainer.x + 8f;
            if (targetX + popupWidth > rootWidth - 4f)
            {
                targetX = mouseInContainer.x - popupWidth - 8f;
            }
            targetX = Mathf.Clamp(targetX, 4f, Mathf.Max(4f, rootWidth - popupWidth - 4f));

            var targetY = mouseInContainer.y + 8f;
            if (targetY + popupHeight > rootHeight - 4f)
            {
                targetY = mouseInContainer.y - popupHeight - 8f;
            }
            targetY = Mathf.Clamp(targetY, 4f, Mathf.Max(4f, rootHeight - popupHeight - 4f));

            popup.style.left = targetX;
            popup.style.top = targetY;
        }

        private void SelectPickedObject(GameObject pickedObj)
        {
            if (pickedObj != null && _context != null)
            {
                _context.SelectedGameObject = pickedObj;
                _context.InspectorVisible = true;
            }

            HidePickPopup();
            if (_context != null)
            {
                _context.PickModeActive = false;
            }
        }

        private List<PickTarget> CollectPickTargets(Vector2 mousePos)
        {
            var targets = new List<PickTarget>();
            var addedIds = new HashSet<int>();

            void AddTarget(GameObject go, string kind)
            {
                if (go == null)
                {
                    return;
                }
                var id = go.GetInstanceID();
                if (addedIds.Add(id))
                {
                    targets.Add(
                        new PickTarget
                        {
                            GameObject = go,
                            Kind = kind,
                        }
                    );
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
            var matchingUI = new List<(GameObject go, int depth, float area, float hitDistance, int sortingOrder, bool isRaycastTarget)>();

            var graphics = Object.FindObjectsOfType<UnityEngine.UI.Graphic>();
            foreach (var graphic in graphics)
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy || IsGameObjectInDevSuite(graphic.gameObject))
                {
                    continue;
                }

                var rect = graphic.rectTransform;
                if (rect == null)
                {
                    continue;
                }

                var canvas = graphic.canvas ?? graphic.GetComponentInParent<Canvas>();
                if (TryMatchRectTransform(rect, canvas, mousePos, out var hitDistance, out var sortingOrder))
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

                    matchingUI.Add((graphic.gameObject, depth, area, hitDistance, sortingOrder, graphic.raycastTarget));
                }
            }

            // Also check Selectables (Buttons, Toggles, etc.) that might not have a Graphic directly on their GameObject
            var selectables = Object.FindObjectsOfType<UnityEngine.UI.Selectable>();
            foreach (var selectable in selectables)
            {
                if (selectable == null || !selectable.gameObject.activeInHierarchy || IsGameObjectInDevSuite(selectable.gameObject))
                {
                    continue;
                }

                var rect = selectable.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                if (matchingUI.Exists(m => m.go == selectable.gameObject))
                {
                    continue;
                }

                var canvas = rect.GetComponentInParent<Canvas>();
                if (TryMatchRectTransform(rect, canvas, mousePos, out var hitDistance, out var sortingOrder))
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

                    matchingUI.Add((selectable.gameObject, depth, area, hitDistance, sortingOrder, true));
                }
            }

            matchingUI.Sort(
                (a, b) =>
                {
                    // For 3D world UI: closer distance from camera comes first (tolerance 0.01f)
                    var distDiff = a.hitDistance - b.hitDistance;
                    if (Mathf.Abs(distDiff) > 0.01f)
                    {
                        return distDiff.CompareTo(0f);
                    }

                    // Higher sorting order first
                    var sortDiff = b.sortingOrder.CompareTo(a.sortingOrder);
                    if (sortDiff != 0)
                    {
                        return sortDiff;
                    }

                    // Prefer raycastTarget true over false
                    var raycastDiff = b.isRaycastTarget.CompareTo(a.isRaycastTarget);
                    if (raycastDiff != 0)
                    {
                        return raycastDiff;
                    }

                    // Deeper hierarchy depth first
                    var d = b.depth.CompareTo(a.depth);
                    if (d != 0)
                    {
                        return d;
                    }

                    // Smaller area first (specific elements over parent containers)
                    return a.area.CompareTo(b.area);
                }
            );

            foreach (var item in matchingUI)
            {
                AddTarget(item.go, "UI");
            }

            // 3. Physics (3D and 2D) across cameras
            var cameras = Camera.allCameras;
            if (cameras == null || cameras.Length == 0)
            {
                var main = Camera.main;
                if (main != null)
                {
                    cameras = new[]
                    {
                        main,
                    };
                }
            }

            if (cameras != null)
            {
                foreach (var cam in cameras)
                {
                    if (cam == null || !cam.gameObject.activeInHierarchy || !cam.enabled)
                    {
                        continue;
                    }

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

        private static bool TryMatchRectTransform(RectTransform rect, Canvas canvas, Vector2 mousePos, out float hitDistance, out int sortingOrder)
        {
            hitDistance = float.MaxValue;
            sortingOrder = 0;

            if (rect == null)
            {
                return false;
            }

            if (canvas == null)
            {
                canvas = rect.GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                return false;
            }

            sortingOrder = canvas.sortingOrder;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, null))
                {
                    hitDistance = 0f;
                    return true;
                }
                return false;
            }

            // ScreenSpaceCamera or WorldSpace:
            // 1. Try canvas worldCamera or rootCanvas worldCamera
            var primaryCam = canvas.worldCamera != null ? canvas.worldCamera : canvas.rootCanvas != null ? canvas.rootCanvas.worldCamera : null;
            if (primaryCam != null && primaryCam.isActiveAndEnabled)
            {
                if (IsScreenPointInRectTransform(rect, mousePos, primaryCam, out hitDistance))
                {
                    return true;
                }
            }

            // 2. Try Camera.main
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.isActiveAndEnabled && mainCam != primaryCam)
            {
                if (IsScreenPointInRectTransform(rect, mousePos, mainCam, out hitDistance))
                {
                    return true;
                }
            }

            // 3. Try any other active camera in scene
            var cameras = Camera.allCameras;
            if (cameras != null)
            {
                foreach (var cam in cameras)
                {
                    if (cam != null && cam.isActiveAndEnabled && cam != primaryCam && cam != mainCam)
                    {
                        if (IsScreenPointInRectTransform(rect, mousePos, cam, out hitDistance))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsScreenPointInRectTransform(RectTransform rect, Vector2 screenPoint, Camera cam, out float hitDistance)
        {
            hitDistance = float.MaxValue;
            if (rect == null || cam == null)
            {
                return false;
            }

            // 1. Double-sided 3D ray-plane intersection against rect plane
            var ray = cam.ScreenPointToRay(screenPoint);
            var rectForward = rect.forward;
            var rectPos = rect.position;

            var dot = Vector3.Dot(ray.direction, rectForward);
            if (Mathf.Abs(dot) > 1e-5f)
            {
                var enter = Vector3.Dot(rectPos - ray.origin, rectForward) / dot;
                if (enter > 0f && enter >= cam.nearClipPlane && (cam.farClipPlane <= 0f || enter <= cam.farClipPlane))
                {
                    var worldPoint = ray.origin + ray.direction * enter;
                    var localPoint = rect.InverseTransformPoint(worldPoint);
                    if (rect.rect.Contains(localPoint))
                    {
                        hitDistance = enter;
                        return true;
                    }
                }
            }

            // 2. Standard RectTransformUtility check
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, cam))
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var center = (corners[0] + corners[2]) * 0.5f;
                hitDistance = Vector3.Distance(cam.transform.position, center);
                return true;
            }

            // 3. Fallback: 2D Screen-space projected quad polygon test
            var worldCorners = new Vector3[4];
            rect.GetWorldCorners(worldCorners);

            var s0 = cam.WorldToScreenPoint(worldCorners[0]);
            var s1 = cam.WorldToScreenPoint(worldCorners[1]);
            var s2 = cam.WorldToScreenPoint(worldCorners[2]);
            var s3 = cam.WorldToScreenPoint(worldCorners[3]);

            if (s0.z > 0f && s1.z > 0f && s2.z > 0f && s3.z > 0f)
            {
                if (IsPointInQuad(screenPoint, s0, s1, s2, s3))
                {
                    hitDistance = (s0.z + s1.z + s2.z + s3.z) * 0.25f;
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointInTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            var dX = p.x - p2.x;
            var dY = p.y - p2.y;
            var dX21 = p2.x - p1.x;
            var dY12 = p1.y - p2.y;
            var d = dY12 * (p0.x - p2.x) + dX21 * (p0.y - p2.y);
            if (Mathf.Abs(d) < 1e-6f)
            {
                return false;
            }
            var s = dY12 * dX + dX21 * dY;
            var t = (p2.y - p0.y) * dX + (p0.x - p2.x) * dY;
            if (d > 0f)
            {
                return s >= 0f && t >= 0f && (s + t) <= d;
            }
            return s <= 0f && t <= 0f && (s + t) >= d;
        }

        private static bool IsPointInQuad(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            return IsPointInTriangle(p, a, b, c) || IsPointInTriangle(p, a, c, d);
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
    }
}