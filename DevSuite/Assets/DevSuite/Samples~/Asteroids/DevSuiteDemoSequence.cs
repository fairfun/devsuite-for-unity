using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Ff.DevSuite;
using Ff.DevSuite.Commands;
using Ff.DevSuite.View;

namespace Ff.DevSuite.Samples.Asteroids
{
    public class DevSuiteDemoSequence : MonoBehaviour
    {
        public static DevSuiteDemoSequence Instance { get; private set; }

        private Coroutine _demoCoroutine;
        private VirtualCursorOverlay _cursor;
        private string _targetSubtitle = "";
        private string _displayedSubtitle = "";
        private float _subtitleAlpha = 0f;
        private bool _isFadingOut = false;
        private GUIStyle _subtitleStyle;

        public bool IsRunning => _demoCoroutine != null;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            Instance = null;
            StopDemo();
        }

        public void StartDemo()
        {
            if (_demoCoroutine != null)
            {
                StopCoroutine(_demoCoroutine);
            }
            _demoCoroutine = StartCoroutine(RunDemoCoroutine());
        }

        public void StopDemo()
        {
            if (_demoCoroutine != null)
            {
                StopCoroutine(_demoCoroutine);
                _demoCoroutine = null;
            }

            if (_cursor != null)
            {
                _cursor.ClearHover();
                _cursor.SetVisible(false);
            }
            DevSuiteUiUtils.DismissActiveTooltip();
            SetSubtitle("");
        }

        public void SetSubtitle(string text)
        {
            _targetSubtitle = text ?? "";
            Debug.Log($"[DevSuiteDemo] {_targetSubtitle}");
        }

        private void Update()
        {
            if (_isFadingOut)
            {
                _subtitleAlpha = Mathf.MoveTowards(_subtitleAlpha, 0f, Time.unscaledDeltaTime / 0.18f);
                if (_subtitleAlpha <= 0f)
                {
                    _displayedSubtitle = _targetSubtitle;
                    _isFadingOut = false;
                }
            }
            else
            {
                if (_displayedSubtitle != _targetSubtitle)
                {
                    if (string.IsNullOrEmpty(_displayedSubtitle))
                    {
                        _displayedSubtitle = _targetSubtitle;
                    }
                    else
                    {
                        _isFadingOut = true;
                    }
                }
                else if (!string.IsNullOrEmpty(_displayedSubtitle))
                {
                    _subtitleAlpha = Mathf.MoveTowards(_subtitleAlpha, 1f, Time.unscaledDeltaTime / 0.22f);
                }
                else
                {
                    _subtitleAlpha = Mathf.MoveTowards(_subtitleAlpha, 0f, Time.unscaledDeltaTime / 0.18f);
                }
            }
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_displayedSubtitle) || _subtitleAlpha <= 0f)
            {
                return;
            }

            GUI.depth = -9999;

            if (_subtitleStyle == null)
            {
                _subtitleStyle = new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                };
            }

            var width = Mathf.Min(Screen.width - 40f, 960f);
            var content = new GUIContent(_displayedSubtitle);
            var textHeight = _subtitleStyle.CalcHeight(content, width);

            var yOffset = Mathf.Lerp(10f, 0f, Mathf.SmoothStep(0f, 1f, _subtitleAlpha));
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - textHeight - 20f + yOffset, width, textHeight);

            _subtitleStyle.normal.textColor = new Color(1f, 204f / 255f, 0f, _subtitleAlpha);
            GUI.Label(rect, content, _subtitleStyle);
        }

        private VisualElement GetElement(string elementName)
        {
            var panelUI = DevSuitePanelUI.Instance;
            var doc = panelUI.GetComponent<UIDocument>();
            return doc.rootVisualElement.Q(elementName);
        }

        private Vector2 PanelToScreen(VisualElement elem)
        {
            return VirtualCursorOverlay.PanelToScreen(elem);
        }

        private Vector2 GetElementScreenPos(string elementName)
        {
            var elem = GetElement(elementName);
            return PanelToScreen(elem);
        }

        private IEnumerator RunDemoCoroutine()
        {
            _cursor = VirtualCursorOverlay.GetOrCreate();
            _cursor.SetVisible(true);

            var context = DevSuiteContext.DefaultInternal;
            var asteroids = AsteroidsGame.Instance;
            var panelUI = DevSuitePanelUI.Instance;
            var doc = panelUI.GetComponent<UIDocument>();

            var centerPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _cursor.SetPositionInstant(centerPos);

            SetSubtitle("Welcome to DevSuite! Interactive in-game debugging toolkit for Unity.");
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Expand DevSuite with the corner button or hotkey (Ctrl + `).");
            var expandElem = GetElement("expand-btn");
            yield return _cursor.MoveTo(expandElem);
            yield return _cursor.Click(expandElem, () => context.PanelExpanded = true);
            yield return new WaitForSecondsRealtime(1.0f);

            yield return _cursor.MoveTo(asteroids.OrientationButton);
            yield return _cursor.Click(
                asteroids.OrientationButton,
                () =>
                {
                    asteroids.PortraitMode = true;
                }
            );

            SetSubtitle("Responsive layouts: seamlessly switches between Portrait and Landscape modes.");
            yield return new WaitForSecondsRealtime(1.0f);

            yield return _cursor.MoveTo(asteroids.OrientationButton);
            yield return _cursor.Click(
                asteroids.OrientationButton,
                () =>
                {
                    asteroids.PortraitMode = false;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Helpful tooltips everywhere! All panels can also open as native Unity Editor Windows.");
            string[] tooltipsSequence =
            {
                "pinned-commands-btn", "commands-btn", "metrics-btn", "inspector-btn", "hierarchy-btn", "logs-btn",
            };
            foreach (var btnName in tooltipsSequence)
            {
                var elem = GetElement(btnName);
                yield return _cursor.MoveTo(elem);
                DevSuiteUiUtils.ShowTooltip(doc.rootVisualElement, elem.tooltip, elem.worldBound.center, false);
                yield return new WaitForSecondsRealtime(1.0f);
            }
            DevSuiteUiUtils.DismissActiveTooltip();
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Commands Panel: Organize cheats, properties, and actions via clean attributes.");
            var commandsElem = GetElement("commands-btn");
            yield return _cursor.MoveTo(commandsElem);
            yield return _cursor.Click(
                commandsElem,
                () =>
                {
                    context.CommandsVisible = true;
                    context.MetricsVisible = false;
                    context.LogsVisible = false;
                    context.HierarchyVisible = false;
                    context.InspectorVisible = false;
                    context.SelectedCategory = "Asteroids Game";
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var spawnWaveElem = doc.rootVisualElement.Query<Button>().Where(b => b.text == "Spawn Wave").First();
            yield return _cursor.MoveTo(spawnWaveElem);
            yield return _cursor.Click(
                spawnWaveElem,
                () =>
                {
                    asteroids.SpawnOneAsteroid(4);
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Execute actions, tweak values with live reflection, and trigger gameplay events.");
            var addScoreElem = doc.rootVisualElement.Query<Button>().Where(b => b.text == "Add Score").First();
            yield return _cursor.MoveTo(addScoreElem);
            yield return _cursor.Click(
                addScoreElem,
                () =>
                {
                    asteroids.AddScore(500);
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Pins Panel: Keep frequent commands and values always accessible.");
            var shipSpeedRow = doc.rootVisualElement.Query(className: "ff-commands-command-row")
                .Where(row => row.Query<Label>(className: "ff-commands-command-label").First().text == "shipSpeed").First();
            var pinIcon = shipSpeedRow.Query<Button>(className: "ff-commands-command-icon").First();
            yield return _cursor.MoveTo(pinIcon);
            yield return _cursor.Click(
                pinIcon,
                () =>
                {
                    Command shipSpeedCmd = null;
                    foreach (var kvp in context.Commands)
                    {
                        if (kvp.Value.Id == "_shipSpeed" || kvp.Value.DisplayName == "shipSpeed")
                        {
                            shipSpeedCmd = kvp.Value;
                            break;
                        }
                    }

                    if (shipSpeedCmd != null && !context.GetPinnedCommands(false).Contains(shipSpeedCmd))
                    {
                        context.TogglePinItem(shipSpeedCmd, true);
                    }
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var pinsElem = GetElement("pinned-commands-btn");
            yield return _cursor.MoveTo(pinsElem);
            yield return _cursor.Click(
                pinsElem,
                () =>
                {
                    context.PinnedCommandsVisible = true;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var pinnedContainer = doc.rootVisualElement.Q("pinned-commands-container");
            var pinnedItem = pinnedContainer.Query(className: "ff-commands-command-row").First();
            var slider = pinnedItem != null ? pinnedItem.Query<Slider>().First() : null;
            var sliderPos = slider != null ? PanelToScreen(slider) : PanelToScreen(pinnedItem);
            if (slider != null)
            {
                sliderPos.x += slider.worldBound.width * 0.15f;
            }
            yield return _cursor.MoveTo(sliderPos);
            yield return _cursor.Click(
                () =>
                {
                    if (slider != null)
                    {
                        slider.value = 0.62f;
                    }
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            commandsElem = GetElement("commands-btn");
            yield return _cursor.MoveTo(commandsElem);
            yield return _cursor.Click(
                commandsElem,
                () =>
                {
                    context.CommandsVisible = false;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Performance Panel: Real-time telemetry for FPS, CPU/GPU, memory & draw calls.");
            var metricsElem = GetElement("metrics-btn");
            yield return _cursor.MoveTo(metricsElem);
            yield return _cursor.Click(
                metricsElem,
                () =>
                {
                    context.MetricsVisible = true;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var graphs = doc.rootVisualElement.Query<PerformanceGraphView>().ToList();
            if (graphs.Count > 0)
            {
                var graph0 = graphs[0];
                yield return _cursor.MoveTo(graph0);
                DevSuiteUiUtils.ShowTooltip(doc.rootVisualElement, graph0.tooltip, graph0.worldBound.center, false);
                yield return new WaitForSecondsRealtime(1.0f);
                yield return _cursor.Click(
                    graph0,
                    () =>
                    {
                        graph0.ToggleCollapsed();
                    }
                );
                yield return new WaitForSecondsRealtime(1.0f);
                DevSuiteUiUtils.DismissActiveTooltip();
            }

            if (graphs.Count > 1)
            {
                var graph1 = graphs[1];
                yield return _cursor.MoveTo(graph1);
                DevSuiteUiUtils.ShowTooltip(doc.rootVisualElement, graph1.tooltip, graph1.worldBound.center, false);
                yield return new WaitForSecondsRealtime(1.0f);
                yield return _cursor.Click(
                    graph1,
                    () =>
                    {
                        graph1.ToggleCollapsed();
                    }
                );
                yield return new WaitForSecondsRealtime(1.0f);
                DevSuiteUiUtils.DismissActiveTooltip();
            }

            yield return _cursor.MoveTo(metricsElem);
            yield return _cursor.Click(
                metricsElem,
                () =>
                {
                    context.MetricsVisible = false;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Hierarchy & Inspector: Two-way editor sync, live reflection, and visual frame selection.");
            var hierarchyElem = GetElement("hierarchy-btn");
            yield return _cursor.MoveTo(hierarchyElem);
            yield return _cursor.Click(
                hierarchyElem,
                () =>
                {
                    context.HierarchyVisible = true;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var inspectorElem = GetElement("inspector-btn");
            yield return _cursor.MoveTo(inspectorElem);
            yield return _cursor.Click(
                inspectorElem,
                () =>
                {
                    context.InspectorVisible = true;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var ship = FindObjectOfType<PlayerShip>();
            var hierarchyItem = doc.rootVisualElement.Query<Label>(className: "hierarchy-item-label").Where(l => l.text == "Ship").First()
                ?? doc.rootVisualElement.Query(className: "hierarchy-item-row").First();
            if (hierarchyItem != null)
            {
                yield return _cursor.MoveTo(hierarchyItem);
                yield return _cursor.Click(
                    hierarchyItem,
                    () =>
                    {
                        if (ship != null)
                        {
                            context.SelectedGameObject = ship.gameObject;
                        }
                    }
                );
            }
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Auto-Pause on selection lets you freeze and inspect any GameObject state.");
            var autoPauseElem = doc.rootVisualElement.Q("autoPauseBtn");
            if (autoPauseElem != null)
            {
                yield return _cursor.MoveTo(autoPauseElem);
                yield return new WaitForSecondsRealtime(1.0f);
            }

            SetSubtitle("Logs Panel & CLI: Filter logs by severity/regex and execute CLI cheats.");
            var logsElem = GetElement("logs-btn");
            yield return _cursor.MoveTo(logsElem);
            yield return _cursor.Click(
                logsElem,
                () =>
                {
                    context.HierarchyVisible = false;
                    context.InspectorVisible = false;
                    context.LogsVisible = true;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var cliInputElem = GetElement("cliInputField");
            yield return _cursor.MoveTo(cliInputElem);
            yield return _cursor.Click(
                cliInputElem,
                () =>
                {
                    context.RequestFocusCli();
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("Type CLI commands with smart autocomplete and parameters.");
            var cliTextField = cliInputElem as TextField;
            const string cliText = "spawn_wave 30";
            for (var i = 1; i <= cliText.Length; i++)
            {
                if (cliTextField != null)
                {
                    cliTextField.value = cliText.Substring(0, i);
                }
                yield return new WaitForSecondsRealtime(0.04f);
            }
            yield return new WaitForSecondsRealtime(1.0f);

            var cliSendElem = GetElement("cliSendButton");
            yield return _cursor.MoveTo(cliSendElem);
            yield return _cursor.Click(
                cliSendElem,
                () =>
                {
                    context.ExecuteCliCommand("spawn_wave 30");
                    if (cliTextField != null)
                    {
                        cliTextField.value = string.Empty;
                    }
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            var closeElem = GetElement("expand-btn");
            yield return _cursor.MoveTo(closeElem);
            yield return _cursor.Click(
                closeElem,
                () =>
                {
                    context.PanelExpanded = false;
                    context.LogsVisible = false;
                }
            );
            yield return new WaitForSecondsRealtime(1.0f);

            SetSubtitle("DevSuite Demo Complete! Instant productivity for Unity WebGL & Editor. Press F2 anytime.");
            yield return new WaitForSecondsRealtime(2.0f);

            _cursor.ClearHover();
            _cursor.SetVisible(false);
            StopDemo();
        }
    }
}