using Ff.DevSuite.Performance;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class PerformanceGraphView : VisualElement
    {
        private int _maxValuesCount;
        private BaseGraphDataProvider _dataProvider;
        private DevSuiteContext _context;
        private BaseGraphDataProvider.DataPoint[] _dataPointsBuffer;
        private int _dataPointsCount;
        private int _dataPointsHead;

        private Vertex[] _verticesBuffer;
        private ushort[] _indicesBuffer;

        private static readonly Color ColorGood = new(118 / 255f, 194 / 255f, 37 / 255f);           // #79af55
        private static readonly Color ColorBad = new(194 / 255f, 53 / 255f, 37 / 255f);             // #ca413c
        private static readonly Color ColorReference = new(148 / 255f, 53 / 255f, 30 / 255f, 0.5f); // #ca413c99

        private const string IconPinned = "\ue259";
        private const string IconUnpinned = "\ue2b6";

        private readonly Label _infoLabel;
        private readonly Button _pinButton;
        private float _lastUpdateTime = -1f;
        private bool _isClickThrough;
        private bool _alwaysExtended;
        private bool _isPinnedMode;

        internal bool ClickThrough => _isClickThrough;

        internal bool AlwaysExtended
        {
            get => _alwaysExtended;
            set
            {
                if (_alwaysExtended != value)
                {
                    _alwaysExtended = value;
                    UpdateViewState();
                }
            }
        }

        internal bool IsPinnedMode
        {
            get => _isPinnedMode;
            set
            {
                if (_isPinnedMode != value)
                {
                    _isPinnedMode = value;
                    MarkDirtyRepaint();
                }
            }
        }

        internal bool IsCollapsed => !_alwaysExtended && _context != null && _context.IsPerformanceGraphCollapsed(_dataProvider);

        public PerformanceGraphView(BaseGraphDataProvider dataProvider, DevSuiteContext context = null)
        {
            _dataProvider = dataProvider;
            _context = context;

            SetMaxValuesCount(_context?.PerformanceGraphTicksCapacity ?? _dataProvider?.CurrentCounterCapacity ?? BaseGraphDataProvider.CounterLength);

            AddToClassList("graph-view");
            style.overflow = Overflow.Hidden;
            tooltip = _dataProvider?.Settings?.Tooltip;

            _infoLabel = new Label
            {
                pickingMode = PickingMode.Ignore,
            };
            _infoLabel.AddToClassList("ff-performance-graph-info");
            Add(_infoLabel);

            _pinButton = new Button();
            _pinButton.AddToClassList("ff-performance-graph-pin-btn");
            _pinButton.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            _pinButton.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            _pinButton.clicked += () =>
            {
                if (_context != null && _dataProvider != null)
                {
                    _context.TogglePerformanceGraphPinned(_dataProvider);
                }
            };
            Add(_pinButton);

            RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    if (_isClickThrough)
                    {
                        return;
                    }

                    if (evt.button == 0)
                    {
                        if (evt.target == _pinButton || _pinButton.Contains(evt.target as VisualElement))
                        {
                            return;
                        }
                        ToggleCollapsed();
                        evt.StopPropagation();
                    }
                }
            );

            Subscribe();
            generateVisualContent += OnGenerateVisualContent;
            UpdateViewState();
        }

        public void Initialize(DevSuiteContext context)
        {
            _context = context;
            SetMaxValuesCount(_context?.PerformanceGraphTicksCapacity ?? _dataProvider?.CurrentCounterCapacity ?? BaseGraphDataProvider.CounterLength);
            Subscribe();
            UpdateViewState();
        }

        internal void ToggleCollapsed()
        {
            if (_alwaysExtended)
            {
                return;
            }

            var isCollapsed = _context.IsPerformanceGraphCollapsed(_dataProvider);
            _context.SetPerformanceGraphCollapsed(_dataProvider, !isCollapsed);
            UpdateViewState();
        }

        private void SetMaxValuesCount(int count)
        {
            if (count < 2)
            {
                count = 2;
            }

            if (_maxValuesCount == count && _dataPointsBuffer != null)
            {
                return;
            }

            _maxValuesCount = count;
            _dataPointsBuffer = new BaseGraphDataProvider.DataPoint[_maxValuesCount];
            _dataPointsCount = 0;
            _dataPointsHead = 0;
            _verticesBuffer = new Vertex[(_maxValuesCount + 1) * 4];
            _indicesBuffer = CreateIndicesBuffer((_maxValuesCount + 1) * 6);
        }

        internal void UpdateViewState()
        {
            var maxCount = _context?.PerformanceGraphTicksCapacity ?? _dataProvider?.CurrentCounterCapacity ?? BaseGraphDataProvider.CounterLength;
            if (_maxValuesCount != maxCount)
            {
                SetMaxValuesCount(maxCount);
            }

            tooltip = _isClickThrough ? null : _dataProvider?.Settings?.Tooltip;
            var isCollapsed = IsCollapsed;
            if (isCollapsed)
            {
                AddToClassList("graph-view--collapsed");
                _infoLabel.text = $"+ {_dataProvider.Label}";
            }
            else
            {
                RemoveFromClassList("graph-view--collapsed");
                if (_dataPointsCount > 0)
                {
                    var lastPoint = _dataPointsBuffer[(_dataPointsHead - 1 + _maxValuesCount) % _maxValuesCount];
                    UpdateInfoLabel(lastPoint);
                }
                else
                {
                    _infoLabel.text = $"- {_dataProvider.Label}";
                }
            }

            if (_pinButton != null)
            {
                var isPinned = _context != null && _context.IsPerformanceGraphPinned(_dataProvider);
                _pinButton.text = isPinned ? IconPinned : IconUnpinned;
                _pinButton.EnableInClassList("pinned", isPinned);
                _pinButton.tooltip = isPinned ? "Unpin graph" : "Pin graph to screen when DevSuite is collapsed";
                if (_isClickThrough)
                {
                    _pinButton.style.display = DisplayStyle.None;
                    _pinButton.pickingMode = PickingMode.Ignore;
                }
            }

            MarkDirtyRepaint();
        }

        internal void SetClickThrough(bool clickThrough)
        {
            _isClickThrough = clickThrough;
            _isPinnedMode = clickThrough;
            if (clickThrough)
            {
                _alwaysExtended = true;
            }

            SetPickingModeRecursive(this, clickThrough ? PickingMode.Ignore : PickingMode.Position);
            if (_infoLabel != null)
            {
                _infoLabel.pickingMode = PickingMode.Ignore;
            }

            if (_pinButton != null)
            {
                _pinButton.style.display = clickThrough ? DisplayStyle.None : DisplayStyle.Flex;
                if (clickThrough)
                {
                    _pinButton.pickingMode = PickingMode.Ignore;
                }
            }

            tooltip = clickThrough ? null : _dataProvider?.Settings?.Tooltip;
            UpdateViewState();
        }

        private static void SetPickingModeRecursive(VisualElement element, PickingMode mode)
        {
            if (element == null)
            {
                return;
            }

            element.pickingMode = mode;
            for (var i = 0; i < element.childCount; i++)
            {
                SetPickingModeRecursive(element[i], mode);
            }
        }

        private readonly StringBuilder _labelStringBuilder = new();
        private (double? val, string str) _referenceValueCached;

        private void UpdateInfoLabel(BaseGraphDataProvider.DataPoint point)
        {
            var min = point.MinValue;
            var max = point.MaxValue;
            var average = point.AverageValue;
            var reference = point.ReferenceValue;

            _labelStringBuilder.Clear();
            _labelStringBuilder.Append("- ");
            _labelStringBuilder.Append(_dataProvider.Label);
            if (reference != null)
            {
                _labelStringBuilder.Append(" / ");
                if (_referenceValueCached.val != reference)
                {
                    _referenceValueCached = (reference, reference.Value.ToString("0.0"));
                }
                _labelStringBuilder.Append(_referenceValueCached.str);
                _labelStringBuilder.Append(' ');
                _labelStringBuilder.AppendLine(_dataProvider.UnitName);
            }
            else
            {
                _labelStringBuilder.Append('\n');
            }

            _labelStringBuilder.Append("Min: ");
            _labelStringBuilder.Append(min.ToString("0.0"));
            _labelStringBuilder.Append(' ');
            _labelStringBuilder.AppendLine(_dataProvider.UnitName);

            _labelStringBuilder.Append("Avg: ");
            _labelStringBuilder.Append(average.ToString("0.0"));
            _labelStringBuilder.Append(' ');
            _labelStringBuilder.AppendLine(_dataProvider.UnitName);

            _labelStringBuilder.Append("Max: ");
            _labelStringBuilder.Append(max.ToString("0.0"));
            _labelStringBuilder.Append(' ');
            _labelStringBuilder.Append(_dataProvider.UnitName);

            _infoLabel.text = _labelStringBuilder.ToString();
        }

        internal void AddValue(BaseGraphDataProvider.DataPoint point)
        {
            if (IsCollapsed)
            {
                return;
            }

            if (_dataPointsBuffer == null || _maxValuesCount <= 0)
            {
                return;
            }

            _dataPointsBuffer[_dataPointsHead] = point;
            _dataPointsHead = (_dataPointsHead + 1) % _maxValuesCount;
            if (_dataPointsCount < _maxValuesCount)
            {
                _dataPointsCount++;
            }

            if (Time.unscaledTime - _lastUpdateTime > 0.5f)
            {
                _lastUpdateTime = Time.unscaledTime;
                UpdateInfoLabel(point);
            }

            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (IsCollapsed)
            {
                return;
            }

            if (_dataPointsBuffer == null || _dataPointsCount == 0 || _maxValuesCount <= 0)
            {
                return;
            }

            var rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0)
            {
                return;
            }

            var maxOfValues = double.MinValue;
            var startIdx = (_dataPointsHead - _dataPointsCount + _maxValuesCount) % _maxValuesCount;
            var curIdx = startIdx;
            for (var i = 0; i < _dataPointsCount; i++)
            {
                var val = _dataPointsBuffer[curIdx].CurrentValue;
                if (val > maxOfValues)
                {
                    maxOfValues = val;
                }
                curIdx = (curIdx + 1) % _maxValuesCount;
            }

            const float topPadding = 1.05f;
            const float minRefScale = 1.3f;

            var lastIdx = (_dataPointsHead - 1 + _maxValuesCount) % _maxValuesCount;
            var lastPoint = _dataPointsBuffer[lastIdx];
            var refValue = lastPoint.ReferenceValue;

            var maxValue = maxOfValues;
            if (refValue.HasValue)
            {
                maxValue = Math.Max(maxValue, refValue.Value * minRefScale);
            }

            maxValue *= topPadding;
            if (maxValue <= 0)
            {
                maxValue = 1f;
            }

            var referenceValue = refValue ?? maxOfValues;
            var barWidth = rect.width / _maxValuesCount;

            var mesh = mgc.Allocate(_verticesBuffer.Length, _indicesBuffer.Length);

            var colorImpact = lastPoint.ReferenceColorImpact ?? 1f;

            curIdx = startIdx;
            var dataStartIndex = _maxValuesCount - _dataPointsCount;

            for (var i = 0; i < _maxValuesCount; i++)
            {
                var vOffset = i * 4;
                var xMin = i * barWidth;
                var xMax = xMin + barWidth;
                var yMin = rect.height;
                var yMax = rect.height;
                var finalColor = Color.clear;

                if (i >= dataStartIndex)
                {
                    var dp = _dataPointsBuffer[curIdx];
                    var val = dp.CurrentValue;
                    var heightPercent = (float)(val / maxValue);
                    var barHeight = rect.height * heightPercent;

                    double currentToReference;
                    if (colorImpact < 0)
                    {
                        if (val >= referenceValue || referenceValue <= 0)
                        {
                            currentToReference = 0;
                        }
                        else
                        {
                            var absImpact = 0.5f * Math.Abs(colorImpact);
                            var ratio = Math.Max(val, 0.00001) / referenceValue;
                            currentToReference = Math.Clamp(Math.Pow(1.0 / ratio, absImpact) - 1f, 0f, 1f);
                        }
                    }
                    else
                    {
                        if (val <= referenceValue || referenceValue <= 0)
                        {
                            currentToReference = 0;
                        }
                        else
                        {
                            var halfImpact = 0.5f * colorImpact;
                            currentToReference = Math.Clamp(Math.Pow(val / referenceValue, halfImpact) - 1f, 0f, 1f);
                        }
                    }

                    finalColor = Color.Lerp(ColorGood, ColorBad, (float)currentToReference);
                    if (_isPinnedMode)
                    {
                        finalColor.a = 0.65f;
                    }

                    yMin = rect.height - barHeight;
                    curIdx = (curIdx + 1) % _maxValuesCount;
                }

                _verticesBuffer[vOffset + 0] = new Vertex
                {
                    position = new Vector3(xMin, yMin, 0),
                    tint = finalColor,
                    uv = Vector2.zero,
                };
                _verticesBuffer[vOffset + 1] = new Vertex
                {
                    position = new Vector3(xMax, yMin, 0),
                    tint = finalColor,
                    uv = Vector2.zero,
                };
                _verticesBuffer[vOffset + 2] = new Vertex
                {
                    position = new Vector3(xMax, yMax, 0),
                    tint = finalColor,
                    uv = Vector2.zero,
                };
                _verticesBuffer[vOffset + 3] = new Vertex
                {
                    position = new Vector3(xMin, yMax, 0),
                    tint = finalColor,
                    uv = Vector2.zero,
                };
            }

            // Reference Line (last quad in buffer)
            var refVOffset = _maxValuesCount * 4;
            if (refValue.HasValue)
            {
                var refHeightPercent = (float)(referenceValue / maxValue);
                var refY = rect.height - (rect.height * refHeightPercent);
                const float refThickness = 2f;

                var refColor = _isPinnedMode
                    ? new Color(ColorReference.r, ColorReference.g, ColorReference.b, ColorReference.a * 0.7f)
                    : ColorReference;

                _verticesBuffer[refVOffset + 0] = new Vertex
                {
                    position = new Vector3(0, refY, 0),
                    tint = refColor,
                    uv = Vector2.zero,
                };
                _verticesBuffer[refVOffset + 1] = new Vertex
                {
                    position = new Vector3(rect.width, refY, 0),
                    tint = refColor,
                    uv = Vector2.zero,
                };
                _verticesBuffer[refVOffset + 2] = new Vertex
                {
                    position = new Vector3(rect.width, refY + refThickness, 0),
                    tint = refColor,
                    uv = Vector2.zero,
                };
                _verticesBuffer[refVOffset + 3] = new Vertex
                {
                    position = new Vector3(0, refY + refThickness, 0),
                    tint = refColor,
                    uv = Vector2.zero,
                };
            }
            else
            {
                // Invisible reference line
                for (var j = 0; j < 4; j++)
                {
                    _verticesBuffer[refVOffset + j] = default;
                }
            }

            mesh.SetAllVertices(_verticesBuffer);
            mesh.SetAllIndices(_indicesBuffer);
        }

        public void Reset()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            Unsubscribe();
            _dataProvider.OnUpdate += HandleDataProviderUpdate;
            _context.OnPerformanceGraphCollapsedChanged += HandleCollapsedChanged;
            _context.OnPerformanceGraphPinnedChanged += HandlePinnedChanged;
        }

        private void Unsubscribe()
        {
            _dataProvider.OnUpdate -= HandleDataProviderUpdate;
            _context.OnPerformanceGraphCollapsedChanged -= HandleCollapsedChanged;
            _context.OnPerformanceGraphPinnedChanged -= HandlePinnedChanged;
        }

        private void HandleCollapsedChanged(BaseGraphDataProvider provider, bool isCollapsed)
        {
            if (provider == _dataProvider)
            {
                UpdateViewState();
            }
        }

        private void HandlePinnedChanged(BaseGraphDataProvider provider, bool isPinned)
        {
            if (provider == _dataProvider)
            {
                UpdateViewState();
            }
        }

        private void HandleDataProviderUpdate(BaseGraphDataProvider.DataPoint dataPoint)
        {
            AddValue(dataPoint);
        }

        private static ushort[] CreateIndicesBuffer(int count)
        {
            var indices = new ushort[count];
            for (int i = 0, v = 0; i < count; i += 6, v += 4)
            {
                indices[i + 0] = (ushort)(v + 0);
                indices[i + 1] = (ushort)(v + 1);
                indices[i + 2] = (ushort)(v + 2);
                indices[i + 3] = (ushort)(v + 2);
                indices[i + 4] = (ushort)(v + 3);
                indices[i + 5] = (ushort)(v + 0);
            }
            return indices;
        }
    }
}