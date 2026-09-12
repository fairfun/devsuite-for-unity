using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite
{
    internal static class DevSuiteUiUtils
    {
        public static void ShowIconButtonClickedFeedback(Button button)
        {
            if (button.userData is string)
            {
                return; // already showing feedback
            }

            var originalIcon = button.text;
            button.userData = originalIcon;
            button.text = "\uf00c";
            button.schedule.Execute(
                () =>
                {
                    button.text = originalIcon;
                    button.userData = null;
                }
            ).StartingIn(500);
        }

        public static bool IsVisible(this VisualElement e)
        {
            return e.visible && e.style.display != DisplayStyle.None &&
                   (e.parent == null || IsVisible(e.parent));
        }

        public static void SetupInputFieldFocus(TextField textField)
        {
            textField.focusable = false;
            textField.tabIndex = -1;

            void ConfigureTextInput(VisualElement input)
            {
                if (input != null)
                {
                    input.tabIndex = -1;
                    input.focusable = false;
                }
            }

            var textInput = textField.Q("unity-text-input");
            if (textInput != null)
            {
                ConfigureTextInput(textInput);
            }
            else
            {
                textField.RegisterCallback<AttachToPanelEvent>(
                    evt =>
                    {
                        ConfigureTextInput(textField.Q("unity-text-input"));
                    }
                );
            }

            textField.RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    textField.focusable = true;
                    var input = textField.Q("unity-text-input");
                    input.focusable = true;
                },
                TrickleDown.TrickleDown
            );

            textField.RegisterCallback<FocusOutEvent>(
                evt =>
                {
                    textField.focusable = false;
                    var input = textField.Q("unity-text-input");
                    input.focusable = false;
                }
            );
        }

        public const float DefaultUnity2022MouseWheelScrollSize = 1000f;

        public static void SetupScrollView(ScrollView scrollView)
        {
            // Mouse wheel speed in Unity 6 works fine natively, but in Unity 2022 it is terribly slow in Player/Runtime by default.
            // In Unity Editor windows (ContextType.Editor), the default scroll size is already correct and applying Player scroll size causes extreme speed.
#if UNITY_6000_0_OR_NEWER
            return;
#endif
            var scrollSize = DefaultUnity2022MouseWheelScrollSize;

            var defaultScrollSize = scrollView.mouseWheelScrollSize > 0f
                ? scrollView.mouseWheelScrollSize
                : 18f;

            void Apply(IPanel panel)
            {
                if (panel != null && panel.contextType == ContextType.Editor)
                {
                    scrollView.mouseWheelScrollSize = defaultScrollSize;
                    return;
                }

                scrollView.mouseWheelScrollSize = scrollSize;
            }

            Apply(scrollView.panel);

            scrollView.RegisterCallback<AttachToPanelEvent>(evt => Apply(evt.destinationPanel));
            scrollView.RegisterCallback<DetachFromPanelEvent>(_ => scrollView.mouseWheelScrollSize = defaultScrollSize);
        }

        private static Label _activeTooltipLabel;
        private static Action _dismissActiveTooltip;

        private static void DismissActiveTooltip()
        {
            if (_activeTooltipLabel != null)
            {
                _activeTooltipLabel.RemoveFromHierarchy();
                _activeTooltipLabel = null;
            }

            if (_dismissActiveTooltip != null)
            {
                var dismiss = _dismissActiveTooltip;
                _dismissActiveTooltip = null;
                dismiss();
            }
        }

        private static bool IsTouchPointer(IPointerEvent evt)
        {
            if (evt.pointerType == UnityEngine.UIElements.PointerType.touch)
            {
                return true;
            }

            if (Application.isMobilePlatform)
            {
                return true;
            }

            if (Input.touchCount > 0)
            {
                return true;
            }

            if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
            {
                return false;
            }

            return evt.pointerId != PointerId.mousePointerId;
        }

        private static bool IsMousePointer(IPointerEvent evt)
        {
            return !IsTouchPointer(evt);
        }

        private static VisualElement FindTooltipElement(VisualElement target, VisualElement root)
        {
            while (target != null && target != root)
            {
                if (!string.IsNullOrEmpty(target.tooltip))
                {
                    return target;
                }
                target = target.parent;
            }
            return null;
        }

        private static Label CreateTooltipLabel()
        {
            var tooltipLabel = new Label();
            tooltipLabel.enableRichText = true;
            tooltipLabel.AddToClassList("ff-tooltip");
            tooltipLabel.style.position = Position.Absolute;
            tooltipLabel.pickingMode = PickingMode.Ignore;
            tooltipLabel.style.display = DisplayStyle.Flex;
            tooltipLabel.style.visibility = Visibility.Hidden;

            // Apply theme styling programmatically to ensure it works across all panels
            tooltipLabel.style.backgroundColor = new Color(42 / 255f, 42 / 255f, 42 / 255f, 0.95f);
            tooltipLabel.style.color = new Color(238 / 255f, 238 / 255f, 238 / 255f, 1f);
            tooltipLabel.style.paddingLeft = 10;
            tooltipLabel.style.paddingRight = 10;
            tooltipLabel.style.paddingTop = 6;
            tooltipLabel.style.paddingBottom = 6;
            tooltipLabel.style.borderBottomLeftRadius = 4;
            tooltipLabel.style.borderBottomRightRadius = 4;
            tooltipLabel.style.borderTopLeftRadius = 4;
            tooltipLabel.style.borderTopRightRadius = 4;
            tooltipLabel.style.borderLeftWidth = 1;
            tooltipLabel.style.borderRightWidth = 1;
            tooltipLabel.style.borderTopWidth = 1;
            tooltipLabel.style.borderBottomWidth = 1;
            tooltipLabel.style.borderLeftColor = new Color(68 / 255f, 68 / 255f, 68 / 255f, 1f);
            tooltipLabel.style.borderRightColor = new Color(68 / 255f, 68 / 255f, 68 / 255f, 1f);
            tooltipLabel.style.borderTopColor = new Color(68 / 255f, 68 / 255f, 68 / 255f, 1f);
            tooltipLabel.style.borderBottomColor = new Color(68 / 255f, 68 / 255f, 68 / 255f, 1f);
            tooltipLabel.style.fontSize = 12;
            tooltipLabel.style.whiteSpace = WhiteSpace.Normal;
            tooltipLabel.style.maxWidth = 300;

            return tooltipLabel;
        }

        private static Label ShowTooltip(VisualElement root, string text, Vector2 pointerPosition, bool isTouch, Func<Vector2> getCurrentPointerPosition = null)
        {
            DismissActiveTooltip();

            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var isEditor = root.panel != null && root.panel.contextType == ContextType.Editor;
            var targetContainer = isEditor ? root : (GetTopRoot(root) ?? root);
            if (targetContainer == null)
            {
                return null;
            }

            var tooltipLabel = CreateTooltipLabel();
            tooltipLabel.text = text;

            targetContainer.Add(tooltipLabel);
            tooltipLabel.BringToFront();

            _activeTooltipLabel = tooltipLabel;

            UpdateTooltipPosition(tooltipLabel, pointerPosition, isTouch);

            tooltipLabel.schedule.Execute(() =>
            {
                if (tooltipLabel.parent != null)
                {
                    var pos = getCurrentPointerPosition != null ? getCurrentPointerPosition() : pointerPosition;
                    UpdateTooltipPosition(tooltipLabel, pos, isTouch);
                    tooltipLabel.style.visibility = Visibility.Visible;
                }
            });

            return tooltipLabel;
        }

        public static void SetupTooltips(VisualElement root)
        {
            VisualElement currentTooltipElement = null;
            IVisualElementScheduledItem tooltipTask = null;
            var lastPointerPosition = Vector2.zero;

            var touchStartPosition = Vector2.zero;
            VisualElement touchTooltipElement = null;
            var touchTooltipShown = false;
            var suppressNextClick = false;

            root.RegisterCallback<DetachFromPanelEvent>(
                _ =>
                {
                    tooltipTask?.Pause();
                    tooltipTask = null;
                    touchTooltipElement = null;
                    touchTooltipShown = false;
                    suppressNextClick = false;
                    currentTooltipElement = null;
                    DismissActiveTooltip();
                }
            );

            root.RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    var isTouch = IsTouchPointer(evt);
                    if (!isTouch)
                    {
                        tooltipTask?.Pause();
                        tooltipTask = null;
                        currentTooltipElement = null;
                        DismissActiveTooltip();
                        return;
                    }

                    // Touch input:
                    DismissActiveTooltip();
                    tooltipTask?.Pause();
                    tooltipTask = null;
                    touchTooltipShown = false;
                    suppressNextClick = false;
                    touchTooltipElement = null;

                    var target = evt.target as VisualElement;
                    var tooltipElement = FindTooltipElement(target, root);
                    if (tooltipElement != null)
                    {
                        touchStartPosition = evt.position;
                        touchTooltipElement = tooltipElement;
                        var tooltipText = tooltipElement.tooltip;

                        tooltipTask = root.schedule.Execute(
                            () =>
                            {
                                if (touchTooltipElement == null || touchTooltipElement.panel == null || root.panel == null)
                                {
                                    return;
                                }

                                touchTooltipShown = true;
                                ShowTooltip(root, tooltipText, touchStartPosition, true);

                                _dismissActiveTooltip = () =>
                                {
                                    touchTooltipShown = false;
                                    touchTooltipElement = null;
                                };
                            }
                        ).StartingIn(500);
                    }
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<PointerMoveEvent>(
                evt =>
                {
                    var isTouch = IsTouchPointer(evt);
                    if (!isTouch)
                    {
                        lastPointerPosition = evt.position;
                        if (_activeTooltipLabel != null && currentTooltipElement != null)
                        {
                            UpdateTooltipPosition(_activeTooltipLabel, lastPointerPosition, false);
                        }
                        return;
                    }

                    // Touch input:
                    if (touchTooltipElement != null)
                    {
                        var moveDist = Vector2.Distance(evt.position, touchStartPosition);
                        // If moved beyond 12px touch slop threshold, cancel pending long-press or hide active tooltip
                        if (moveDist > 12f)
                        {
                            tooltipTask?.Pause();
                            tooltipTask = null;
                            touchTooltipElement = null;

                            if (touchTooltipShown)
                            {
                                touchTooltipShown = false;
                                DismissActiveTooltip();
                            }
                        }
                    }
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<PointerUpEvent>(
                evt =>
                {
                    // Any pointer up (finger lifted or mouse button released) unconditionally cancels pending tooltip!
                    tooltipTask?.Pause();
                    tooltipTask = null;

                    var isTouch = IsTouchPointer(evt);
                    if (isTouch)
                    {
                        touchTooltipElement = null;

                        if (touchTooltipShown)
                        {
                            touchTooltipShown = false;
                            suppressNextClick = true;
                            DismissActiveTooltip();
                            evt.StopImmediatePropagation();
                            evt.PreventDefault();
                        }
                    }
                    else
                    {
                        currentTooltipElement = null;
                        DismissActiveTooltip();
                    }
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<ClickEvent>(
                evt =>
                {
                    // A click event always cancels any pending tooltip task
                    tooltipTask?.Pause();
                    tooltipTask = null;
                    touchTooltipElement = null;

                    if (suppressNextClick)
                    {
                        suppressNextClick = false;
                        evt.StopImmediatePropagation();
                        evt.PreventDefault();
                    }
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<PointerCancelEvent>(
                evt =>
                {
                    tooltipTask?.Pause();
                    tooltipTask = null;
                    touchTooltipElement = null;
                    touchTooltipShown = false;
                    suppressNextClick = false;
                    DismissActiveTooltip();
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<PointerOverEvent>(
                evt =>
                {
                    if (IsTouchPointer(evt))
                    {
                        return; // Touch NEVER uses hover tooltips (only long-press)
                    }

                    lastPointerPosition = evt.position;
                    var target = evt.target as VisualElement;
                    var tooltipElement = FindTooltipElement(target, root);

                    if (tooltipElement != null)
                    {
                        if (currentTooltipElement != tooltipElement)
                        {
                            tooltipTask?.Pause();
                            DismissActiveTooltip();

                            currentTooltipElement = tooltipElement;
                            var tooltipText = tooltipElement.tooltip;

                            tooltipTask = root.schedule.Execute(
                                () =>
                                {
                                    if (currentTooltipElement != tooltipElement || tooltipElement.panel == null || root.panel == null)
                                    {
                                        return;
                                    }

                                    ShowTooltip(root, tooltipText, lastPointerPosition, false, () => lastPointerPosition);
                                    _dismissActiveTooltip = () =>
                                    {
                                        currentTooltipElement = null;
                                    };
                                }
                            ).StartingIn(400);
                        }
                    }
                    else
                    {
                        tooltipTask?.Pause();
                        tooltipTask = null;
                        currentTooltipElement = null;
                        DismissActiveTooltip();
                    }
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<PointerOutEvent>(
                evt =>
                {
                    if (IsTouchPointer(evt))
                    {
                        return;
                    }

                    if (currentTooltipElement != null)
                    {
                        var target = evt.target as VisualElement;
                        if (target == currentTooltipElement)
                        {
                            if (!currentTooltipElement.worldBound.Contains(evt.position))
                            {
                                tooltipTask?.Pause();
                                tooltipTask = null;
                                currentTooltipElement = null;
                                DismissActiveTooltip();
                            }
                        }
                    }
                },
                TrickleDown.TrickleDown
            );

            root.RegisterCallback<PointerLeaveEvent>(
                evt =>
                {
                    if (!IsTouchPointer(evt))
                    {
                        tooltipTask?.Pause();
                        tooltipTask = null;
                        currentTooltipElement = null;
                        DismissActiveTooltip();
                    }
                }
            );
        }

        public static VisualElement GetTopRoot(VisualElement element)
        {
            if (element == null)
            {
                return null;
            }
            if (element.panel?.visualTree != null)
            {
                return element.panel.visualTree;
            }
            var topRoot = element;
            while (topRoot.parent != null)
            {
                topRoot = topRoot.parent;
            }
            return topRoot;
        }

        private static void UpdateTooltipPosition(Label tooltipLabel, Vector2 pointerPosition, bool isTouch)
        {
            if (tooltipLabel == null || tooltipLabel.parent == null)
            {
                return;
            }

            var parent = tooltipLabel.parent;
            var isEditor = parent.panel != null && parent.panel.contextType == ContextType.Editor;
            var topRoot = isEditor ? parent : (GetTopRoot(parent) ?? parent);

            var rootWidth = topRoot.layout.width;
            if (float.IsNaN(rootWidth) || rootWidth <= 0)
            {
                rootWidth = topRoot.resolvedStyle.width;
            }
            if (!isEditor && (float.IsNaN(rootWidth) || rootWidth <= 0) && topRoot.panel?.visualTree != null)
            {
                rootWidth = topRoot.panel.visualTree.layout.width;
                if (float.IsNaN(rootWidth) || rootWidth <= 0)
                {
                    rootWidth = topRoot.panel.visualTree.resolvedStyle.width;
                }
            }
            if (float.IsNaN(rootWidth) || rootWidth <= 0)
            {
                rootWidth = parent.layout.width;
            }
            if (float.IsNaN(rootWidth) || rootWidth <= 0)
            {
                rootWidth = Screen.width > 0 ? Screen.width : 800f;
            }

            var rootHeight = topRoot.layout.height;
            if (float.IsNaN(rootHeight) || rootHeight <= 0)
            {
                rootHeight = topRoot.resolvedStyle.height;
            }
            if (!isEditor && (float.IsNaN(rootHeight) || rootHeight <= 0) && topRoot.panel?.visualTree != null)
            {
                rootHeight = topRoot.panel.visualTree.layout.height;
                if (float.IsNaN(rootHeight) || rootHeight <= 0)
                {
                    rootHeight = topRoot.panel.visualTree.resolvedStyle.height;
                }
            }
            if (float.IsNaN(rootHeight) || rootHeight <= 0)
            {
                rootHeight = parent.layout.height;
            }
            if (float.IsNaN(rootHeight) || rootHeight <= 0)
            {
                rootHeight = Screen.height > 0 ? Screen.height : 600f;
            }

            var tooltipWidth = tooltipLabel.layout.width;
            if (float.IsNaN(tooltipWidth) || tooltipWidth <= 0)
            {
                tooltipWidth = tooltipLabel.resolvedStyle.width;
                if (float.IsNaN(tooltipWidth) || tooltipWidth <= 0)
                {
                    var text = tooltipLabel.text ?? "";
                    tooltipWidth = Mathf.Clamp((text.Length * 7.5f) + 24f, 60f, 300f);
                }
            }

            var tooltipHeight = tooltipLabel.layout.height;
            if (float.IsNaN(tooltipHeight) || tooltipHeight <= 0)
            {
                tooltipHeight = tooltipLabel.resolvedStyle.height;
                if (float.IsNaN(tooltipHeight) || tooltipHeight <= 0)
                {
                    var text = tooltipLabel.text ?? "";
                    var lines = text.Split('\n');
                    var lineCount = 0;
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var lineLen = lines[i].Length;
                        lineCount += Mathf.Max(1, Mathf.CeilToInt((lineLen * 7.2f) / 270f));
                    }
                    tooltipHeight = Mathf.Max(28f, (lineCount * 17f) + 14f);
                }
            }

            var posInTopRoot = topRoot.WorldToLocal(pointerPosition);

            float targetX;
            float targetY;

            if (isTouch)
            {
                targetX = posInTopRoot.x - (tooltipWidth * 0.5f);

                targetY = posInTopRoot.y - tooltipHeight - 24f;
                if (targetY < 4f)
                {
                    targetY = posInTopRoot.y + 36f;
                }
            }
            else
            {
                targetX = posInTopRoot.x + 12f;
                if (targetX + tooltipWidth > rootWidth - 4f)
                {
                    targetX = posInTopRoot.x - tooltipWidth - 12f;
                }

                targetY = posInTopRoot.y + 12f;
                if (targetY + tooltipHeight > rootHeight - 4f)
                {
                    targetY = posInTopRoot.y - tooltipHeight - 12f;
                }
            }

            targetX = Mathf.Clamp(targetX, 4f, Mathf.Max(4f, rootWidth - tooltipWidth - 4f));
            targetY = Mathf.Clamp(targetY, 4f, Mathf.Max(4f, rootHeight - tooltipHeight - 4f));

            var targetInParent = topRoot.ChangeCoordinatesTo(parent, new Vector2(targetX, targetY));

            tooltipLabel.style.left = targetInParent.x;
            tooltipLabel.style.top = targetInParent.y;
        }
    }
}