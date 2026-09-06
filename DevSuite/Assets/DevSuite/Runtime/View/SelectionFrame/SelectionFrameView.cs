using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class SelectionFrameView : VisualElement
    {
        private DevSuiteContext _context;
        private bool _isActive;

        private readonly VisualElement _tagsContainer;
        private readonly List<SelectionTagElement> _tagPool = new();

        private static readonly Color Color3D = new(0.22f, 0.74f, 0.97f, 0.9f); // Cyan #38bdf8
        private static readonly Color Color3DFill = new(0.22f, 0.74f, 0.97f, 0.05f);
        private static readonly Color Color2D = new(0.06f, 0.72f, 0.51f, 0.9f); // Emerald #10b981
        private static readonly Color Color2DFill = new(0.06f, 0.72f, 0.51f, 0.05f);
        private static readonly Color ColorUI = new(0.96f, 0.62f, 0.04f, 0.9f); // Amber #f59e0b
        private static readonly Color ColorUIFill = new(0.96f, 0.62f, 0.04f, 0.05f);
        private static readonly Color ColorDefault = new(0.8f, 0.8f, 0.85f, 0.9f);
        private static readonly Color ColorDefaultFill = new(0.8f, 0.8f, 0.85f, 0.05f);

        private static readonly (int, int)[] BoxEdges =
        {
            (0, 1), (1, 2), (2, 3), (3, 0), // Bottom
            (4, 5), (5, 6), (6, 7), (7, 4), // Top
            (0, 4), (1, 5), (2, 6), (3, 7), // Vertical pillars
        };

        public SelectionFrameView(StyleSheet uss)
        {
            AddToClassList("devsuite-selection-frame-overlay");
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            style.width = Length.Percent(100);
            style.height = Length.Percent(100);
            if (uss != null)
            {
                styleSheets.Add(uss);
            }

            _tagsContainer = new VisualElement();
            _tagsContainer.name = "TagsContainer";
            _tagsContainer.pickingMode = PickingMode.Ignore;
            _tagsContainer.style.position = Position.Absolute;
            _tagsContainer.style.left = 0;
            _tagsContainer.style.top = 0;
            _tagsContainer.style.right = 0;
            _tagsContainer.style.bottom = 0;
            _tagsContainer.style.width = Length.Percent(100);
            _tagsContainer.style.height = Length.Percent(100);
            Add(_tagsContainer);

            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<AttachToPanelEvent>(_ => SendToBack());
        }

        public void Initialize(DevSuiteContext context)
        {
            _context = context;
            if (_context != null)
            {
                _context.OnEveryFrame += HandleOnEveryFrame;
                _context.OnChanged += HandleContextChanged;
            }
        }

        public void Reset()
        {
            if (_context != null)
            {
                _context.OnEveryFrame -= HandleOnEveryFrame;
                _context.OnChanged -= HandleContextChanged;
                _context = null;
            }

            _isActive = false;
            HideAllTags();
            MarkDirtyRepaint();
        }

        private void HandleContextChanged()
        {
            UpdateState();
        }

        private void HandleOnEveryFrame()
        {
            UpdateState();
        }

        private void UpdateState()
        {
            if (_context == null || !_context.IsSelectedFromDevSuite || !_context.ShowSelectionFrame)
            {
                if (_isActive)
                {
                    _isActive = false;
                    HideAllTags();
                    MarkDirtyRepaint();
                }
                return;
            }

            var selectedObjects = _context.SelectedGameObjects;
            if (selectedObjects == null || selectedObjects.Count == 0)
            {
                if (_isActive)
                {
                    _isActive = false;
                    HideAllTags();
                    MarkDirtyRepaint();
                }
                return;
            }

            var hasActive = false;
            for (var i = 0; i < selectedObjects.Count; i++)
            {
                var go = selectedObjects[i];
                if (go != null && go.activeInHierarchy)
                {
                    hasActive = true;
                    break;
                }
            }

            if (!hasActive)
            {
                if (_isActive)
                {
                    _isActive = false;
                    HideAllTags();
                    MarkDirtyRepaint();
                }
                return;
            }

            _isActive = true;
            UpdateTags(selectedObjects);
            MarkDirtyRepaint();
        }

        private void UpdateTags(IReadOnlyList<GameObject> selectedObjects)
        {
            var tagIndex = 0;
            for (var i = 0; i < selectedObjects.Count; i++)
            {
                var go = selectedObjects[i];
                if (go == null || !go.activeInHierarchy)
                {
                    continue;
                }

                UpdateObjectTag(go, ref tagIndex);
            }

            for (var i = tagIndex; i < _tagPool.Count; i++)
            {
                _tagPool[i].style.display = DisplayStyle.None;
            }
        }

        private void UpdateObjectTag(GameObject go, ref int tagIndex)
        {
            // 1. Check uGUI RectTransform
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                if (TryGetRectTransformBounds(rectTransform, out var frameRect))
                {
                    ShowTag(ref tagIndex, go.name, "UI", "tag-ui", frameRect);
                }
                return;
            }

            // 2. Check 2D SpriteRenderer
            var spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.enabled)
            {
                if (TryGet2DBounds(go, spriteRenderer.bounds, out var frameRect))
                {
                    ShowTag(ref tagIndex, go.name, "2D", "tag-2d", frameRect);
                }
                return;
            }

            // 3. Encapsulated bounds
            if (TryGetEncapsulatedBounds(go, out var bounds, out var hasRenderer, out var is2D))
            {
                var cam = FindRenderingCamera(go);
                var use2D = is2D || (cam != null && cam.orthographic) || bounds.size.z < 0.001f;
                if (use2D)
                {
                    if (TryGet2DBounds(go, bounds, out var frameRect))
                    {
                        ShowTag(ref tagIndex, go.name, hasRenderer ? "2D" : "Col", "tag-2d", frameRect);
                    }
                }
                else
                {
                    if (TryGet3DBounds(go, bounds, out var frameRect))
                    {
                        ShowTag(ref tagIndex, go.name, hasRenderer ? "3D" : "Col", "tag-3d", frameRect);
                    }
                }
                return;
            }

            // 4. Empty GameObject fallback
            if (TryGetEmptyGameObjectBounds(go, out var emptyRect))
            {
                ShowTag(ref tagIndex, go.name, "GO", "tag-default", emptyRect);
            }
        }

        private bool TryGetRectTransformBounds(RectTransform rect, out Rect panelRect)
        {
            panelRect = default;
            var canvas = rect.GetComponentInParent<Canvas>();
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var p = ScreenToPanel(new Vector2(corners[i].x, corners[i].y));
                    if (p.x < minX)
                    {
                        minX = p.x;
                    }
                    if (p.x > maxX)
                    {
                        maxX = p.x;
                    }
                    if (p.y < minY)
                    {
                        minY = p.y;
                    }
                    if (p.y > maxY)
                    {
                        maxY = p.y;
                    }
                }
                panelRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
                return true;
            }
            else
            {
                var cam = canvas.worldCamera ?? Camera.main;
                if (cam == null || !cam.isActiveAndEnabled)
                {
                    return false;
                }

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var p = WorldToPanel(corners[i], cam);
                    if (p.x < minX)
                    {
                        minX = p.x;
                    }
                    if (p.x > maxX)
                    {
                        maxX = p.x;
                    }
                    if (p.y < minY)
                    {
                        minY = p.y;
                    }
                    if (p.y > maxY)
                    {
                        maxY = p.y;
                    }
                }
                panelRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
                return true;
            }
        }

        private bool TryGet2DBounds(GameObject go, Bounds bounds, out Rect panelRect)
        {
            panelRect = default;
            var cam = FindRenderingCamera(go);
            if (cam == null || !cam.isActiveAndEnabled)
            {
                return false;
            }

            var center = bounds.center;
            var ext = bounds.extents;

            var extX = Mathf.Max(ext.x, 0.2f);
            var extY = Mathf.Max(ext.y, 0.2f);

            var w0 = new Vector3(center.x - extX, center.y - extY, center.z);
            var w1 = new Vector3(center.x + extX, center.y - extY, center.z);
            var w2 = new Vector3(center.x + extX, center.y + extY, center.z);
            var w3 = new Vector3(center.x - extX, center.y + extY, center.z);

            var sp0 = cam.WorldToScreenPoint(w0);
            var sp1 = cam.WorldToScreenPoint(w1);
            var sp2 = cam.WorldToScreenPoint(w2);
            var sp3 = cam.WorldToScreenPoint(w3);

            if (sp0.z <= 0f && sp1.z <= 0f && sp2.z <= 0f && sp3.z <= 0f)
            {
                return false;
            }

            var p0 = WorldToPanel(w0, cam);
            var p1 = WorldToPanel(w1, cam);
            var p2 = WorldToPanel(w2, cam);
            var p3 = WorldToPanel(w3, cam);

            var minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            var maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            var minY = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            var maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            panelRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private bool TryGet3DBounds(GameObject go, Bounds bounds, out Rect panelRect)
        {
            panelRect = default;
            var cam = FindRenderingCamera(go);
            if (cam == null || !cam.isActiveAndEnabled)
            {
                return false;
            }

            var min = bounds.min;
            var max = bounds.max;
            var worldCorners = new Vector3[8]
            {
                new(min.x, min.y, min.z), new(max.x, min.y, min.z), new(max.x, min.y, max.z), new(min.x, min.y, max.z), new(min.x, max.y, min.z), new(max.x, max.y, min.z), new(max.x, max.y, max.z),
                new(min.x, max.y, max.z),
            };

            var zNear = cam.nearClipPlane + 0.02f;
            float screenMinX = float.MaxValue, screenMaxX = float.MinValue;
            float screenMinY = float.MaxValue, screenMaxY = float.MinValue;
            var anyVisible = false;

            for (var i = 0; i < BoxEdges.Length; i++)
            {
                var (idxA, idxB) = BoxEdges[i];
                var wA = worldCorners[idxA];
                var wB = worldCorners[idxB];

                var lA = cam.transform.InverseTransformPoint(wA);
                var lB = cam.transform.InverseTransformPoint(wB);

                if (lA.z < zNear && lB.z < zNear)
                {
                    continue;
                }

                var finalWA = wA;
                var finalWB = wB;

                if (lA.z < zNear)
                {
                    var t = (zNear - lA.z) / (lB.z - lA.z);
                    var clippedL = Vector3.Lerp(lA, lB, t);
                    finalWA = cam.transform.TransformPoint(clippedL);
                }
                else if (lB.z < zNear)
                {
                    var t = (zNear - lB.z) / (lA.z - lB.z);
                    var clippedL = Vector3.Lerp(lB, lA, t);
                    finalWB = cam.transform.TransformPoint(clippedL);
                }

                var pA = WorldToPanel(finalWA, cam);
                var pB = WorldToPanel(finalWB, cam);

                if (pA.x < screenMinX)
                {
                    screenMinX = pA.x;
                }
                if (pA.y < screenMinY)
                {
                    screenMinY = pA.y;
                }
                if (pB.x < screenMinX)
                {
                    screenMinX = pB.x;
                }
                if (pB.y < screenMinY)
                {
                    screenMinY = pB.y;
                }

                anyVisible = true;
            }

            if (!anyVisible)
            {
                return false;
            }

            const float padding = 4f;
            panelRect = Rect.MinMaxRect(screenMinX - padding, screenMinY - padding, screenMaxX + padding, screenMaxY + padding);
            return true;
        }

        private bool TryGetEmptyGameObjectBounds(GameObject go, out Rect panelRect)
        {
            panelRect = default;
            var cam = FindRenderingCamera(go);
            if (cam == null || !cam.isActiveAndEnabled)
            {
                return false;
            }

            var wPos = go.transform.position;
            var lPos = cam.transform.InverseTransformPoint(wPos);
            if (!cam.orthographic && lPos.z <= cam.nearClipPlane)
            {
                return false;
            }

            var p = WorldToPanel(wPos, cam);
            const float size = 12f;
            panelRect = Rect.MinMaxRect(p.x - size, p.y - size, p.x + size, p.y + size);
            return true;
        }

        private void ShowTag(ref int tagIndex, string name, string kind, string categoryClass, Rect frameRect)
        {
            while (_tagPool.Count <= tagIndex)
            {
                var tagElement = new SelectionTagElement();
                _tagsContainer.Add(tagElement);
                _tagPool.Add(tagElement);
            }

            var tag = _tagPool[tagIndex++];
            tag.Update(name, kind, categoryClass);

            var rootWidth = layout.width > 0 ? layout.width : resolvedStyle.width;
            if (float.IsNaN(rootWidth) || rootWidth <= 0)
            {
                rootWidth = Screen.width;
            }
            var rootHeight = layout.height > 0 ? layout.height : resolvedStyle.height;
            if (float.IsNaN(rootHeight) || rootHeight <= 0)
            {
                rootHeight = Screen.height;
            }

            var posX = Mathf.Clamp(frameRect.xMin, 4f, Mathf.Max(4f, rootWidth - 160f));

            // Default: position header above the top of the frame
            var posY = frameRect.yMin - 24f;
            if (posY < 4f)
            {
                // When frame is at the upper part of the screen / off top edge: move header to the bottom of the frame
                posY = frameRect.yMax + 4f;
            }

            // Ensure header stays within visible screen bounds
            posY = Mathf.Clamp(posY, 4f, Mathf.Max(4f, rootHeight - 26f));

            tag.style.left = posX;
            tag.style.top = posY;
            tag.style.display = DisplayStyle.Flex;
        }

        private void HideAllTags()
        {
            for (var i = 0; i < _tagPool.Count; i++)
            {
                _tagPool[i].style.display = DisplayStyle.None;
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (!_isActive || _context == null || !_context.IsSelectedFromDevSuite || !_context.ShowSelectionFrame)
            {
                return;
            }

            var selectedObjects = _context.SelectedGameObjects;
            if (selectedObjects == null || selectedObjects.Count == 0)
            {
                return;
            }

            var painter = mgc.painter2D;

            for (var i = 0; i < selectedObjects.Count; i++)
            {
                var go = selectedObjects[i];
                if (go == null || !go.activeInHierarchy)
                {
                    continue;
                }

                DrawObjectSelectionFrame(painter, go);
            }
        }

        private void DrawObjectSelectionFrame(Painter2D painter, GameObject go)
        {
            // 1. Check uGUI RectTransform
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                DrawRectTransformFrame(painter, go, rectTransform);
                return;
            }

            // 2. Check 2D SpriteRenderer
            var spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.enabled)
            {
                Draw2DFrame(painter, go, spriteRenderer.bounds, Color2D, Color2DFill);
                return;
            }

            // 3. Try to get encapsulated bounds from Renderers and Colliders
            if (TryGetEncapsulatedBounds(go, out var bounds, out var hasRenderer, out var is2D))
            {
                var cam = FindRenderingCamera(go);
                var use2D = is2D || (cam != null && cam.orthographic) || bounds.size.z < 0.001f;
                if (use2D)
                {
                    Draw2DFrame(painter, go, bounds, Color2D, Color2DFill);
                }
                else
                {
                    Draw3DFrame(painter, go, bounds, Color3D, Color3DFill);
                }
                return;
            }

            // 4. Empty GameObject fallback (locator)
            DrawEmptyGameObjectMarker(painter, go);
        }

        private void DrawRectTransformFrame(Painter2D painter, GameObject go, RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            var panelCorners = new Vector2[4];

            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                for (var i = 0; i < 4; i++)
                {
                    panelCorners[i] = ScreenToPanel(new Vector2(corners[i].x, corners[i].y));
                }
            }
            else
            {
                var cam = canvas.worldCamera ?? Camera.main;
                if (cam == null || !cam.isActiveAndEnabled)
                {
                    return;
                }

                for (var i = 0; i < 4; i++)
                {
                    panelCorners[i] = WorldToPanel(corners[i], cam);
                }
            }

            // Subtle fill
            painter.fillColor = ColorUIFill;
            painter.BeginPath();
            painter.MoveTo(panelCorners[0]);
            painter.LineTo(panelCorners[1]);
            painter.LineTo(panelCorners[2]);
            painter.LineTo(panelCorners[3]);
            painter.ClosePath();
            painter.Fill();

            // Outline
            painter.lineWidth = 1.5f;
            painter.strokeColor = ColorUI;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            painter.MoveTo(panelCorners[0]);
            painter.LineTo(panelCorners[1]);
            painter.LineTo(panelCorners[2]);
            painter.LineTo(panelCorners[3]);
            painter.ClosePath();
            painter.Stroke();

            // Corner brackets
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                if (panelCorners[i].x < minX)
                {
                    minX = panelCorners[i].x;
                }
                if (panelCorners[i].x > maxX)
                {
                    maxX = panelCorners[i].x;
                }
                if (panelCorners[i].y < minY)
                {
                    minY = panelCorners[i].y;
                }
                if (panelCorners[i].y > maxY)
                {
                    maxY = panelCorners[i].y;
                }
            }

            DrawCornerBrackets(painter, minX, minY, maxX, maxY, ColorUI);
        }

        private void Draw2DFrame(Painter2D painter, GameObject go, Bounds bounds, Color strokeColor, Color fillColor)
        {
            var cam = FindRenderingCamera(go);
            if (cam == null || !cam.isActiveAndEnabled)
            {
                return;
            }

            var center = bounds.center;
            var ext = bounds.extents;

            var extX = Mathf.Max(ext.x, 0.2f);
            var extY = Mathf.Max(ext.y, 0.2f);

            var w0 = new Vector3(center.x - extX, center.y - extY, center.z);
            var w1 = new Vector3(center.x + extX, center.y - extY, center.z);
            var w2 = new Vector3(center.x + extX, center.y + extY, center.z);
            var w3 = new Vector3(center.x - extX, center.y + extY, center.z);

            var sp0 = cam.WorldToScreenPoint(w0);
            var sp1 = cam.WorldToScreenPoint(w1);
            var sp2 = cam.WorldToScreenPoint(w2);
            var sp3 = cam.WorldToScreenPoint(w3);

            if (sp0.z <= 0f && sp1.z <= 0f && sp2.z <= 0f && sp3.z <= 0f)
            {
                return; // Behind camera
            }

            var p0 = WorldToPanel(w0, cam);
            var p1 = WorldToPanel(w1, cam);
            var p2 = WorldToPanel(w2, cam);
            var p3 = WorldToPanel(w3, cam);

            // Subtle fill
            painter.fillColor = fillColor;
            painter.BeginPath();
            painter.MoveTo(p0);
            painter.LineTo(p1);
            painter.LineTo(p2);
            painter.LineTo(p3);
            painter.ClosePath();
            painter.Fill();

            // Outline
            painter.lineWidth = 1.5f;
            painter.strokeColor = strokeColor;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            painter.MoveTo(p0);
            painter.LineTo(p1);
            painter.LineTo(p2);
            painter.LineTo(p3);
            painter.ClosePath();
            painter.Stroke();

            var minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            var maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            var minY = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            var maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            DrawCornerBrackets(painter, minX, minY, maxX, maxY, strokeColor);
        }

        private void Draw3DFrame(Painter2D painter, GameObject go, Bounds bounds, Color strokeColor, Color fillColor)
        {
            var cam = FindRenderingCamera(go);
            if (cam == null || !cam.isActiveAndEnabled)
            {
                return;
            }

            var min = bounds.min;
            var max = bounds.max;
            var worldCorners = new Vector3[8]
            {
                new(min.x, min.y, min.z), // 0
                new(max.x, min.y, min.z), // 1
                new(max.x, min.y, max.z), // 2
                new(min.x, min.y, max.z), // 3
                new(min.x, max.y, min.z), // 4
                new(max.x, max.y, min.z), // 5
                new(max.x, max.y, max.z), // 6
                new(min.x, max.y, max.z), // 7
            };

            var zNear = cam.nearClipPlane + 0.02f;
            float screenMinX = float.MaxValue, screenMaxX = float.MinValue;
            float screenMinY = float.MaxValue, screenMaxY = float.MinValue;
            var anyVisible = false;

            painter.lineWidth = 1.5f;
            painter.strokeColor = strokeColor;
            painter.lineCap = LineCap.Round;

            // Draw 12 edges of 3D AABB with near plane clipping
            for (var i = 0; i < BoxEdges.Length; i++)
            {
                var (idxA, idxB) = BoxEdges[i];
                var wA = worldCorners[idxA];
                var wB = worldCorners[idxB];

                var lA = cam.transform.InverseTransformPoint(wA);
                var lB = cam.transform.InverseTransformPoint(wB);

                if (lA.z < zNear && lB.z < zNear)
                {
                    continue; // Entire edge behind near plane
                }

                var finalWA = wA;
                var finalWB = wB;

                if (lA.z < zNear)
                {
                    var t = (zNear - lA.z) / (lB.z - lA.z);
                    var clippedL = Vector3.Lerp(lA, lB, t);
                    finalWA = cam.transform.TransformPoint(clippedL);
                }
                else if (lB.z < zNear)
                {
                    var t = (zNear - lB.z) / (lA.z - lB.z);
                    var clippedL = Vector3.Lerp(lB, lA, t);
                    finalWB = cam.transform.TransformPoint(clippedL);
                }

                var pA = WorldToPanel(finalWA, cam);
                var pB = WorldToPanel(finalWB, cam);

                painter.BeginPath();
                painter.MoveTo(pA);
                painter.LineTo(pB);
                painter.Stroke();

                if (pA.x < screenMinX)
                {
                    screenMinX = pA.x;
                }
                if (pA.x > screenMaxX)
                {
                    screenMaxX = pA.x;
                }
                if (pA.y < screenMinY)
                {
                    screenMinY = pA.y;
                }
                if (pA.y > screenMaxY)
                {
                    screenMaxY = pA.y;
                }

                if (pB.x < screenMinX)
                {
                    screenMinX = pB.x;
                }
                if (pB.x > screenMaxX)
                {
                    screenMaxX = pB.x;
                }
                if (pB.y < screenMinY)
                {
                    screenMinY = pB.y;
                }
                if (pB.y > screenMaxY)
                {
                    screenMaxY = pB.y;
                }

                anyVisible = true;
            }

            if (!anyVisible)
            {
                return;
            }

            // Draw outer corner brackets with padding
            const float padding = 4f;
            screenMinX -= padding;
            screenMaxX += padding;
            screenMinY -= padding;
            screenMaxY += padding;

            // Subtle 2D fill
            painter.fillColor = fillColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(screenMinX, screenMinY));
            painter.LineTo(new Vector2(screenMaxX, screenMinY));
            painter.LineTo(new Vector2(screenMaxX, screenMaxY));
            painter.LineTo(new Vector2(screenMinX, screenMaxY));
            painter.ClosePath();
            painter.Fill();

            DrawCornerBrackets(painter, screenMinX, screenMinY, screenMaxX, screenMaxY, strokeColor);
        }

        private void DrawEmptyGameObjectMarker(Painter2D painter, GameObject go)
        {
            var cam = FindRenderingCamera(go);
            if (cam == null || !cam.isActiveAndEnabled)
            {
                return;
            }

            var wPos = go.transform.position;
            var lPos = cam.transform.InverseTransformPoint(wPos);
            if (!cam.orthographic && lPos.z <= cam.nearClipPlane)
            {
                return;
            }

            var p = WorldToPanel(wPos, cam);

            const float size = 12f;
            var minX = p.x - size;
            var maxX = p.x + size;
            var minY = p.y - size;
            var maxY = p.y + size;

            DrawCornerBrackets(painter, minX, minY, maxX, maxY, ColorDefault);

            painter.lineWidth = 1.5f;
            painter.strokeColor = ColorDefault;
            painter.lineCap = LineCap.Round;

            painter.BeginPath();
            painter.MoveTo(new Vector2(p.x - 4f, p.y));
            painter.LineTo(new Vector2(p.x + 4f, p.y));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(p.x, p.y - 4f));
            painter.LineTo(new Vector2(p.x, p.y + 4f));
            painter.Stroke();
        }

        private void DrawCornerBrackets(Painter2D painter, float minX, float minY, float maxX, float maxY, Color color)
        {
            var width = maxX - minX;
            var height = maxY - minY;
            var bracketLen = Mathf.Clamp(Mathf.Min(width, height) * 0.25f, 6f, 16f);
            const float thickness = 2.0f;

            painter.lineWidth = thickness;
            painter.strokeColor = color;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;

            // Top-Left
            painter.BeginPath();
            painter.MoveTo(new Vector2(minX + bracketLen, minY));
            painter.LineTo(new Vector2(minX, minY));
            painter.LineTo(new Vector2(minX, minY + bracketLen));
            painter.Stroke();

            // Top-Right
            painter.BeginPath();
            painter.MoveTo(new Vector2(maxX - bracketLen, minY));
            painter.LineTo(new Vector2(maxX, minY));
            painter.LineTo(new Vector2(maxX, minY + bracketLen));
            painter.Stroke();

            // Bottom-Left
            painter.BeginPath();
            painter.MoveTo(new Vector2(minX, maxY - bracketLen));
            painter.LineTo(new Vector2(minX, maxY));
            painter.LineTo(new Vector2(minX + bracketLen, maxY));
            painter.Stroke();

            // Bottom-Right
            painter.BeginPath();
            painter.MoveTo(new Vector2(maxX - bracketLen, maxY));
            painter.LineTo(new Vector2(maxX, maxY));
            painter.LineTo(new Vector2(maxX, maxY - bracketLen));
            painter.Stroke();
        }

        private Vector2 WorldToPanel(Vector3 worldPoint, Camera cam)
        {
            if (panel != null && cam != null)
            {
                var panelPos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPoint, cam);
                return this.WorldToLocal(panelPos);
            }

            if (cam != null)
            {
                var sp = cam.WorldToScreenPoint(worldPoint);
                return ScreenToPanel(sp);
            }

            return ScreenToPanel(worldPoint);
        }

        private Vector2 ScreenToPanel(Vector2 screenPoint)
        {
            var screenHeight = Screen.height > 0 ? Screen.height : 600f;
            var flippedScreen = new Vector2(screenPoint.x, screenHeight - screenPoint.y);

            if (panel != null)
            {
                var p = RuntimePanelUtils.ScreenToPanel(panel, flippedScreen);
                return this.WorldToLocal(p);
            }

            var topRoot = DevSuiteUtils.GetTopRoot(this) ?? this;
            var screenWidth = Screen.width > 0 ? Screen.width : 800f;
            var panelWidth = topRoot?.layout.width > 0 ? topRoot.layout.width : topRoot?.resolvedStyle.width > 0 ? topRoot.resolvedStyle.width : screenWidth;
            var panelHeight = topRoot?.layout.height > 0 ? topRoot.layout.height : topRoot?.resolvedStyle.height > 0 ? topRoot.resolvedStyle.height : screenHeight;

            var local = new Vector2(
                flippedScreen.x * (panelWidth / screenWidth),
                flippedScreen.y * (panelHeight / screenHeight)
            );
            return this.WorldToLocal(local);
        }

        private static bool TryGetEncapsulatedBounds(GameObject go, out Bounds bounds, out bool hasRenderer, out bool is2D)
        {
            bounds = default;
            hasRenderer = false;
            is2D = false;
            if (go == null)
            {
                return false;
            }

            var hasBounds = false;

            var renderers = go.GetComponentsInChildren<Renderer>(false);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled)
                {
                    continue;
                }

                if (r is SpriteRenderer)
                {
                    is2D = true;
                }

                var rBounds = r.bounds;
                if (rBounds.size.x <= 0f && rBounds.size.y <= 0f && rBounds.size.z <= 0f)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rBounds;
                    hasBounds = true;
                    hasRenderer = true;
                }
                else
                {
                    bounds.Encapsulate(rBounds);
                }
            }

            var colliders2d = go.GetComponentsInChildren<Collider2D>(false);
            foreach (var c2d in colliders2d)
            {
                if (c2d == null || !c2d.enabled)
                {
                    continue;
                }

                is2D = true;
                var cBounds = c2d.bounds;
                if (cBounds.size.x <= 0f && cBounds.size.y <= 0f && cBounds.size.z <= 0f)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = cBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(cBounds);
                }
            }

            var colliders = go.GetComponentsInChildren<Collider>(false);
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled)
                {
                    continue;
                }

                var cBounds = c.bounds;
                if (cBounds.size.x <= 0f && cBounds.size.y <= 0f && cBounds.size.z <= 0f)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = cBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(cBounds);
                }
            }

            return hasBounds;
        }

        private static Camera FindRenderingCamera(GameObject go)
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled && (main.cullingMask & (1 << go.layer)) != 0)
            {
                return main;
            }

            var cameras = Camera.allCameras;
            if (cameras != null)
            {
                Camera bestCam = null;
                foreach (var cam in cameras)
                {
                    if (cam != null && cam.isActiveAndEnabled && (cam.cullingMask & (1 << go.layer)) != 0)
                    {
                        if (bestCam == null || cam.depth > bestCam.depth)
                        {
                            bestCam = cam;
                        }
                    }
                }
                if (bestCam != null)
                {
                    return bestCam;
                }
            }

            return main ?? (cameras != null && cameras.Length > 0 ? cameras[0] : null);
        }

        private class SelectionTagElement : VisualElement
        {
            private readonly Label _nameLabel;
            private readonly Label _badgeLabel;
            private string _currentCategoryClass;

            public SelectionTagElement()
            {
                AddToClassList("devsuite-selection-tag");
                pickingMode = PickingMode.Ignore;

                _nameLabel = new Label();
                _nameLabel.AddToClassList("devsuite-selection-tag-name");
                _nameLabel.pickingMode = PickingMode.Ignore;
                Add(_nameLabel);

                _badgeLabel = new Label();
                _badgeLabel.AddToClassList("devsuite-selection-tag-badge");
                _badgeLabel.pickingMode = PickingMode.Ignore;
                Add(_badgeLabel);
            }

            public void Update(string name, string kind, string categoryClass)
            {
                _nameLabel.text = name;
                _badgeLabel.text = kind;

                if (_currentCategoryClass != categoryClass)
                {
                    if (!string.IsNullOrEmpty(_currentCategoryClass))
                    {
                        RemoveFromClassList(_currentCategoryClass);
                    }
                    _currentCategoryClass = categoryClass;
                    if (!string.IsNullOrEmpty(_currentCategoryClass))
                    {
                        AddToClassList(_currentCategoryClass);
                    }
                }
            }
        }
    }
}