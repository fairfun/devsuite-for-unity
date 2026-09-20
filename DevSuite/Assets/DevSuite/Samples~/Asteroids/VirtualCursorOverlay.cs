using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Ff.DevSuite.Samples.Asteroids
{
    public class VirtualCursorOverlay : MonoBehaviour
    {
        private static VirtualCursorOverlay _instance;
        public static VirtualCursorOverlay Instance => _instance;

        private static readonly PropertyInfo PseudoStatesProp = typeof(VisualElement).GetProperty(
            "pseudoStates",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        );

        private static readonly int HoverStateVal = GetPseudoStateValue("Hover", 2);
        private static readonly int ActiveStateVal = GetPseudoStateValue("Active", 1);

        private static int GetPseudoStateValue(string name, int fallback)
        {
            try
            {
                var enumType = PseudoStatesProp?.PropertyType ?? typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.PseudoStates");
                if (enumType != null && Enum.IsDefined(enumType, name))
                {
                    return Convert.ToInt32(Enum.Parse(enumType, name));
                }
            }
            catch
            {
            }
            return fallback;
        }

        public static void SetPseudoState(VisualElement elem, bool hover, bool active)
        {
            if (elem == null || PseudoStatesProp == null)
            {
                return;
            }

            try
            {
                int current = Convert.ToInt32(PseudoStatesProp.GetValue(elem));
                if (hover)
                {
                    current |= HoverStateVal;
                }
                else
                {
                    current &= ~HoverStateVal;
                }

                if (active)
                {
                    current |= ActiveStateVal;
                }
                else
                {
                    current &= ~ActiveStateVal;
                }

                PseudoStatesProp.SetValue(elem, Enum.ToObject(PseudoStatesProp.PropertyType, current));
            }
            catch
            {
            }
        }

        public static Vector2 PanelToScreen(VisualElement elem)
        {
            if (elem == null || elem.panel == null)
            {
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            var center = elem.worldBound.center;
            var panel = elem.panel;

            var top = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(0, Screen.height));
            var bottom = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, 0));
            var pWidth = bottom.x - top.x;
            var pHeight = bottom.y - top.y;

            if (pWidth > 0 && pHeight > 0)
            {
                float screenX = (center.x - top.x) / pWidth * Screen.width;
                float screenY = (center.y - top.y) / pHeight * Screen.height;
                return new Vector2(screenX, screenY);
            }

            var vt = panel.visualTree;
            var pw = vt.layout.width > 0 ? vt.layout.width : (vt.resolvedStyle.width > 0 ? vt.resolvedStyle.width : Screen.width);
            var ph = vt.layout.height > 0 ? vt.layout.height : (vt.resolvedStyle.height > 0 ? vt.resolvedStyle.height : Screen.height);
            return new Vector2(center.x * (Screen.width / pw), center.y * (Screen.height / ph));
        }

        public static Vector2 SelectableToScreen(Selectable selectable)
        {
            if (selectable == null)
            {
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            var rt = selectable.GetComponent<RectTransform>();
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * 0.5f;
            return new Vector2(center.x, Screen.height - center.y);
        }

        private class ActiveRipple
        {
            public Vector2 Position;
            public float Elapsed;
            public float Duration;
        }

        private readonly List<ActiveRipple> _ripples = new();
        private Vector2 _currentScreenPos;
        private Vector2 _targetScreenPos;
        private bool _isVisible;
        private float _clickScale = 1f;

        private VisualElement _currentHoverElement;
        private Selectable _currentHoverSelectable;

        private Texture2D _cursorTexture;
        private Texture2D _circleTexture;
        private Texture2D _whitePixel;

        public Vector2 CurrentPosition => _currentScreenPos;

        public static VirtualCursorOverlay GetOrCreate()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("[DevSuiteDemo_VirtualCursor]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<VirtualCursorOverlay>();
            return _instance;
        }

        private void Awake()
        {
            _instance = this;

            _currentScreenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _targetScreenPos = _currentScreenPos;

            InitTextures();
        }

        private void InitTextures()
        {
            _whitePixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whitePixel.SetPixel(0, 0, Color.white);
            _whitePixel.Apply();

            _cursorTexture = GenerateCursorTexture();
            _circleTexture = GenerateCircleTexture(64);
        }

        private Texture2D GenerateCursorTexture()
        {
            string[] pattern =
            {
                "#...............",
                "##..............",
                "#W#.............",
                "#WW#............",
                "#WWW#...........",
                "#WWWW#..........",
                "#WWWW#..........",
                "#WWWWW#.........",
                "#WWWWWW#........",
                "#WWWWWWW#.......",
                "#WWWWWWWW#......",
                "#WWWWWWWWW#.....",
                "#WWWWWW#####....",
                "#WWW#WW#........",
                "#W###WWW#.......",
                "##...#WW#.......",
                "#....#WW#.......",
                "......#WW#......",
                "......#WW#......",
                "......#WWW#.....",
                ".......#W##.....",
                ".......##.......",
            };

            int h = pattern.Length;
            int w = pattern[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int r = 0; r < h; r++)
            {
                int texY = h - 1 - r;
                for (int c = 0; c < w; c++)
                {
                    char ch = pattern[r][c];
                    Color col = ch switch
                    {
                        'W' => Color.white,
                        '#' => Color.black,
                        _ => Color.clear
                    };
                    tex.SetPixel(c, texY, col);
                }
            }

            tex.Apply();
            return tex;
        }

        private Texture2D GenerateCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size * 0.5f;
            float innerRadius = radius - 4f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    if (dist <= radius && dist >= innerRadius)
                    {
                        float alpha = Mathf.Clamp01(radius - dist) * Mathf.Clamp01(dist - innerRadius);
                        tex.SetPixel(x, y, new Color(1f, 0.85f, 0.2f, Mathf.Max(0.5f, alpha)));
                    }
                    else if (dist < innerRadius)
                    {
                        tex.SetPixel(x, y, new Color(1f, 0.85f, 0.2f, 0.25f));
                    }
                    else
                    {
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                }
            }

            tex.Apply();
            return tex;
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (!visible)
            {
                ClearHover();
            }
        }

        public void SetPositionInstant(Vector2 screenPos)
        {
            _currentScreenPos = screenPos;
            _targetScreenPos = screenPos;
        }

        public void ClearHover()
        {
            if (_currentHoverElement != null)
            {
                SetPseudoState(_currentHoverElement, false, false);
                using var leaveEvt = PointerLeaveEvent.GetPooled();
                leaveEvt.target = _currentHoverElement;
                _currentHoverElement.SendEvent(leaveEvt);
                _currentHoverElement = null;
            }

            if (_currentHoverSelectable != null)
            {
                var pointerData = new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(_currentScreenPos.x, Screen.height - _currentScreenPos.y),
                    button = PointerEventData.InputButton.Left
                };
                ExecuteEvents.Execute(_currentHoverSelectable.gameObject, pointerData, ExecuteEvents.pointerExitHandler);
                _currentHoverSelectable = null;
            }
        }

        public void SetHover(VisualElement elem)
        {
            if (_currentHoverElement == elem)
            {
                return;
            }

            ClearHover();

            if (elem != null)
            {
                _currentHoverElement = elem;
                SetPseudoState(_currentHoverElement, true, false);
                using var enterEvt = PointerEnterEvent.GetPooled();
                enterEvt.target = _currentHoverElement;
                _currentHoverElement.SendEvent(enterEvt);
            }
        }

        public void SetHover(Selectable selectable)
        {
            if (_currentHoverSelectable == selectable)
            {
                return;
            }

            ClearHover();

            if (selectable != null)
            {
                _currentHoverSelectable = selectable;
                var pointerData = new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(_currentScreenPos.x, Screen.height - _currentScreenPos.y),
                    button = PointerEventData.InputButton.Left
                };
                ExecuteEvents.Execute(_currentHoverSelectable.gameObject, pointerData, ExecuteEvents.pointerEnterHandler);
            }
        }

        public IEnumerator MoveTo(VisualElement target, float duration = -1f)
        {
            if (target == null)
            {
                yield break;
            }

            var targetPos = PanelToScreen(target);
            yield return MoveToInternal(targetPos, duration);
            SetHover(target);
        }

        public IEnumerator MoveTo(Selectable target, float duration = -1f)
        {
            if (target == null)
            {
                yield break;
            }

            var targetPos = SelectableToScreen(target);
            yield return MoveToInternal(targetPos, duration);
            SetHover(target);
        }

        public IEnumerator MoveTo(Vector2 targetPos, float duration = -1f)
        {
            ClearHover();
            yield return MoveToInternal(targetPos, duration);
        }

        private IEnumerator MoveToInternal(Vector2 targetPos, float duration)
        {
            _targetScreenPos = targetPos;
            var startPos = _currentScreenPos;
            var distance = Vector2.Distance(startPos, targetPos);

            if (duration < 0f)
            {
                var screenDiag = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height);
                var normDist = Mathf.Clamp01(distance / Mathf.Max(1f, screenDiag));
                duration = Mathf.Lerp(0.4f, 1.2f, normDist);
            }
            else
            {
                var screenDiag = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height);
                var normDist = Mathf.Clamp01(distance / Mathf.Max(1f, screenDiag));
                duration = Mathf.Max(duration, Mathf.Lerp(0.35f, 1.1f, normDist));
            }

            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var smoothT = Mathf.SmoothStep(0f, 1f, t);
                _currentScreenPos = Vector2.Lerp(startPos, _targetScreenPos, smoothT);
                yield return null;
            }

            _currentScreenPos = _targetScreenPos;
        }

        public IEnumerator Click(VisualElement elem, Action onClickAction = null)
        {
            if (elem == null)
            {
                onClickAction?.Invoke();
                yield break;
            }

            SetHover(elem);
            SetPseudoState(elem, true, true);

            var prevScale = elem.style.scale;
            elem.style.scale = new Scale(new Vector2(0.94f, 0.94f));

            _clickScale = 0.82f;
            SpawnClickRipple(_currentScreenPos);

            using (var downEvt = PointerDownEvent.GetPooled())
            {
                downEvt.target = elem;
                elem.SendEvent(downEvt);
            }

            yield return new WaitForSecondsRealtime(0.12f);

            elem.style.scale = prevScale;
            SetPseudoState(elem, true, false);
            _clickScale = 1f;

            using (var upEvt = PointerUpEvent.GetPooled())
            {
                upEvt.target = elem;
                elem.SendEvent(upEvt);
            }

            if (onClickAction != null)
            {
                onClickAction.Invoke();
            }
            else
            {
                using var clickEvt = ClickEvent.GetPooled();
                clickEvt.target = elem;
                elem.SendEvent(clickEvt);
            }

            yield return new WaitForSecondsRealtime(0.08f);
        }

        public IEnumerator Click(Selectable selectable, Action onClickAction = null)
        {
            if (selectable == null)
            {
                onClickAction?.Invoke();
                yield break;
            }

            SetHover(selectable);

            var rt = selectable.GetComponent<RectTransform>();
            var originalScale = rt.localScale;
            var originalPos = rt.localPosition;

            var pivot = rt.pivot;
            var size = rt.rect.size;
            var pivotDelta = new Vector2(0.5f - pivot.x, 0.5f - pivot.y);
            var centerOffset = new Vector3(pivotDelta.x * size.x * (1f - 0.94f), pivotDelta.y * size.y * (1f - 0.94f), 0f);

            rt.localScale = originalScale * 0.94f;
            rt.localPosition = originalPos + centerOffset;

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(_currentScreenPos.x, Screen.height - _currentScreenPos.y),
                button = PointerEventData.InputButton.Left
            };

            ExecuteEvents.Execute(selectable.gameObject, pointerData, ExecuteEvents.pointerDownHandler);

            _clickScale = 0.82f;
            SpawnClickRipple(_currentScreenPos);

            yield return new WaitForSecondsRealtime(0.12f);

            rt.localScale = originalScale;
            rt.localPosition = originalPos;
            _clickScale = 1f;

            ExecuteEvents.Execute(selectable.gameObject, pointerData, ExecuteEvents.pointerUpHandler);

            if (onClickAction != null)
            {
                onClickAction.Invoke();
            }
            else
            {
                ExecuteEvents.Execute(selectable.gameObject, pointerData, ExecuteEvents.pointerClickHandler);
            }

            yield return new WaitForSecondsRealtime(0.08f);
        }

        public IEnumerator Click(Action onClickAction = null)
        {
            _clickScale = 0.82f;
            SpawnClickRipple(_currentScreenPos);

            yield return new WaitForSecondsRealtime(0.12f);

            _clickScale = 1f;
            onClickAction?.Invoke();

            yield return new WaitForSecondsRealtime(0.08f);
        }

        private void SpawnClickRipple(Vector2 screenPos)
        {
            _ripples.Add(new ActiveRipple
            {
                Position = screenPos,
                Elapsed = 0f,
                Duration = 0.45f
            });
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var r = _ripples[i];
                r.Elapsed += dt;
                if (r.Elapsed >= r.Duration)
                {
                    _ripples.RemoveAt(i);
                }
            }
        }

        private void OnGUI()
        {
            if (!_isVisible && _ripples.Count == 0)
            {
                return;
            }

            GUI.depth = -10000;

            for (int i = 0; i < _ripples.Count; i++)
            {
                var r = _ripples[i];
                float t = Mathf.Clamp01(r.Elapsed / r.Duration);
                float size = Mathf.Lerp(16f, 64f, t);
                float alpha = Mathf.Lerp(1f, 0f, t);

                var savedColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                var rect = new Rect(r.Position.x - size * 0.5f, r.Position.y - size * 0.5f, size, size);
                GUI.DrawTexture(rect, _circleTexture);
                GUI.color = savedColor;
            }

            if (!_isVisible)
            {
                return;
            }

            float cursorW = 16f * _clickScale;
            float cursorH = 22f * _clickScale;

            var cursorRect = new Rect(
                _currentScreenPos.x,
                _currentScreenPos.y,
                cursorW,
                cursorH
            );

            GUI.DrawTexture(cursorRect, _cursorTexture);
        }

        private void OnDestroy()
        {
            ClearHover();
            _instance = null;

            Destroy(_cursorTexture);
            Destroy(_circleTexture);
            Destroy(_whitePixel);
        }
    }
}
