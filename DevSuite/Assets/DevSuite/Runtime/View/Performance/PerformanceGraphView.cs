using Ff.DevSuite.Performance;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class PerformanceGraphView : VisualElement
    {
        private const int MaxValuesCount = BaseGraphDataProvider.CounterLength;
        private BaseGraphDataProvider _dataProvider;
        private readonly BaseGraphDataProvider.DataPoint[] _dataPointsBuffer = new BaseGraphDataProvider.DataPoint[MaxValuesCount];
        private int _dataPointsCount;
        private int _dataPointsHead;

        private readonly Vertex[] _verticesBuffer = new Vertex[(MaxValuesCount + 1) * 4];
        private readonly ushort[] _indicesBuffer = CreateIndicesBuffer((MaxValuesCount + 1) * 6);

        private static readonly Color ColorGood = new(118 / 255f, 194 / 255f, 37 / 255f);          // #79af55
        private static readonly Color ColorBad = new(194 / 255f, 53 / 255f, 37 / 255f);             // #ca413c
        private static readonly Color ColorReference = new(148 / 255f, 53 / 255f, 30 / 255f, 0.5f); // #ca413c99

        private readonly Label _infoLabel;
        private float _lastUpdateTime = -1f;

        public PerformanceGraphView(BaseGraphDataProvider dataProvider)
        {
            _dataProvider = dataProvider;

            AddToClassList("graph-view");
            style.overflow = Overflow.Hidden;

            _infoLabel = new Label
            {
                pickingMode = PickingMode.Ignore
            };
            _infoLabel.AddToClassList("ff-performance-graph-info");
            Add(_infoLabel);

            Subscribe();
            generateVisualContent += OnGenerateVisualContent;
        }

        private StringBuilder _labelStringBuilder = new();
        private (double? val, string str) _referenceValueCached;
        internal void AddValue(BaseGraphDataProvider.DataPoint point)
        {
            _dataPointsBuffer[_dataPointsHead] = point;
            _dataPointsHead = (_dataPointsHead + 1) % MaxValuesCount;
            if (_dataPointsCount < MaxValuesCount)
            {
                _dataPointsCount++;
            }

            if (Time.unscaledTime - _lastUpdateTime > 0.5f)
            {
                _lastUpdateTime = Time.unscaledTime;

                var min = point.MinValue;
                var max = point.MaxValue;
                var average = point.AverageValue;
                var reference = point.ReferenceValue;

                _labelStringBuilder.Clear();
                _labelStringBuilder.Append(_dataProvider.Label);
                if (reference != null)
                {
                    _labelStringBuilder.Append(" / ");
                    if (_referenceValueCached.val != reference)
                        _referenceValueCached = (reference, reference.Value.ToString("0.0"));
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
                _labelStringBuilder.AppendLine(_dataProvider.UnitName);

                _infoLabel.text = _labelStringBuilder.ToString();
            }

            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_dataPointsCount == 0)
            {
                return;
            }

            var rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0)
            {
                return;
            }

            var maxOfValues = double.MinValue;
            var startIdx = (_dataPointsHead - _dataPointsCount + MaxValuesCount) % MaxValuesCount;
            var curIdx = startIdx;
            for (var i = 0; i < _dataPointsCount; i++)
            {
                var val = _dataPointsBuffer[curIdx].CurrentValue;
                if (val > maxOfValues)
                    maxOfValues = val;
                curIdx = (curIdx + 1) % MaxValuesCount;
            }

            const float topPadding = 1.05f;
            const float minRefScale = 1.3f;

            int lastIdx = (_dataPointsHead - 1 + MaxValuesCount) % MaxValuesCount;
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

            var referenceValue = (refValue ?? maxOfValues);
            var barWidth = rect.width / MaxValuesCount;

            var mesh = mgc.Allocate(_verticesBuffer.Length, _indicesBuffer.Length);

            var colorImpact = lastPoint.ReferenceColorImpact ?? 1f;
            var halfColorImpact = 0.5f * colorImpact;

            curIdx = startIdx;
            var dataStartIndex = MaxValuesCount - _dataPointsCount;

            for (int i = 0; i < MaxValuesCount; i++)
            {
                var vOffset = i * 4;
                float xMin = i * barWidth;
                float xMax = xMin + barWidth;
                float yMin = rect.height;
                float yMax = rect.height;
                Color finalColor = Color.clear;

                if (i >= dataStartIndex)
                {
                    var dp = _dataPointsBuffer[curIdx];
                    var val = dp.CurrentValue;
                    var heightPercent = (float)(val / maxValue);
                    var barHeight = rect.height * heightPercent;

                    var currentToReference = Math.Clamp(Math.Pow(val / referenceValue, halfColorImpact) - 1f, 0f, 1f);
                    finalColor = Color.Lerp(ColorGood, ColorBad, (float)currentToReference);

                    yMin = rect.height - barHeight;
                    curIdx = (curIdx + 1) % MaxValuesCount;
                }

                _verticesBuffer[vOffset + 0] = new Vertex { position = new Vector3(xMin, yMin, 0), tint = finalColor, uv = Vector2.zero };
                _verticesBuffer[vOffset + 1] = new Vertex { position = new Vector3(xMax, yMin, 0), tint = finalColor, uv = Vector2.zero };
                _verticesBuffer[vOffset + 2] = new Vertex { position = new Vector3(xMax, yMax, 0), tint = finalColor, uv = Vector2.zero };
                _verticesBuffer[vOffset + 3] = new Vertex { position = new Vector3(xMin, yMax, 0), tint = finalColor, uv = Vector2.zero };
            }

            // Reference Line (last quad in buffer)
            var refVOffset = MaxValuesCount * 4;
            if (refValue.HasValue)
            {
                var refHeightPercent = (float)(referenceValue / maxValue);
                var refY = rect.height - (rect.height * refHeightPercent);
                const float refThickness = 2f;

                _verticesBuffer[refVOffset + 0] = new Vertex { position = new Vector3(0, refY, 0), tint = ColorReference, uv = Vector2.zero };
                _verticesBuffer[refVOffset + 1] = new Vertex { position = new Vector3(rect.width, refY, 0), tint = ColorReference, uv = Vector2.zero };
                _verticesBuffer[refVOffset + 2] = new Vertex { position = new Vector3(rect.width, refY + refThickness, 0), tint = ColorReference, uv = Vector2.zero };
                _verticesBuffer[refVOffset + 3] = new Vertex { position = new Vector3(0, refY + refThickness, 0), tint = ColorReference, uv = Vector2.zero };
            }
            else
            {
                // Invisible reference line
                for (int j = 0; j < 4; j++) _verticesBuffer[refVOffset + j] = default;
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
        }

        private void Unsubscribe()
        {
            _dataProvider.OnUpdate -= HandleDataProviderUpdate;
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