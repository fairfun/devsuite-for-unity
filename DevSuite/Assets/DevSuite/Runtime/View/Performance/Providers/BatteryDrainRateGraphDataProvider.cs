using System;
using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class BatteryDrainRateGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Apx. Battery Drain Rate";
        internal override string UnitName => "%/h";

        private const string DefaultTooltip =
            "Approximate battery discharge rate in percent per hour (%/h).\n\n" +
            "<b>How it is counted:</b>\n" +
            "Monitors <b><color=#ffc800>SystemInfo.batteryLevel</color></b> (0.0 to 1.0) and <b><color=#ffc800>SystemInfo.batteryStatus</color></b> over <b><color=#ffc800>Time.realtimeSinceStartupAsDouble</color></b>. " +
            "When a discrete drop occurs between readings, calculates <b><color=#ffc800>stepRate = (deltaPercent / deltaTime) * 3600d</color></b> and smooths via exponential moving average: " +
            "<b><color=#ffc800>rate = (rate * 0.6) + (stepRate * 0.4)</color></b>. If no drops occur over time, decays towards <b><color=#ffc800>maxPossibleRate = (1.0 / elapsed) * 3600d</color></b>. " +
            "Returns <b><color=#ffc800>0 %/h</color></b> while charging, full, not discharging, or unsupported.\n\n" +
            "<b>Platform Limitations & Features:</b>\n" +
            "• <b>Android:</b> Levels usually update in 1% steps. OEM battery-saving policies or OS broadcast throttling may delay update notifications by several minutes.\n" +
            "• <b>iOS:</b> Reports in coarse 1% or 5% quantization increments. iOS deliberately batches battery level notifications to conserve power and avoid fingerprinting, delaying step detection.\n" +
            "• <b>Desktop / Laptop:</b> Desktop PCs without batteries report <b><color=#ffc800>-1.0</color></b>. Laptops report ACPI battery percentages with variable OS polling intervals (10–60s).\n" +
            "• <b>Editor / WebGL:</b> Unsupported; returns <b><color=#ffc800>-1.0</color></b> (displays 0).\n" +
            "• <b>Accuracy Note:</b> Because mobile operating systems report discrete quantized steps rather than continuous milliamp measurements, an accurate reading requires waiting for at least two battery level drops during active gameplay.";

        private float _lastBatteryLevel = -1f;
        private double _lastDropTime;
        private double _currentDrainRate;
        private bool _isInitialized;

        public BatteryDrainRateGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 20.0d,
                expandedByDefault: false,
                register: true,
                tooltip: DefaultTooltip
            );
        }

        protected override double GetCurrentValue()
        {
            var level = SystemInfo.batteryLevel;
            var status = SystemInfo.batteryStatus;

            var now = Time.realtimeSinceStartupAsDouble;

            if (level < 0f || status == BatteryStatus.Charging || status == BatteryStatus.NotCharging || status == BatteryStatus.Full)
            {
                _isInitialized = false;
                UpdateValues(level, now, 0d);
                return 0d;
            }

            if (!_isInitialized)
            {
                if (_lastBatteryLevel < 0f || level >= _lastBatteryLevel)
                {
                    UpdateValues(level, now, 0d);
                    return 0d;
                }

                // First drop establishes the baseline
                _isInitialized = true;
                UpdateValues(level, now, 0d);
                return 0d;
            }

            if (level < _lastBatteryLevel)
            {
                var deltaPercent = (_lastBatteryLevel - level) * 100d;
                var deltaTime = now - _lastDropTime;

                if (deltaTime > 0.5d)
                {
                    var stepRate = deltaPercent / deltaTime * 3600d;
                    _currentDrainRate = _currentDrainRate > 0d
                        ? (_currentDrainRate * 0.6d) + (stepRate * 0.4d)
                        : stepRate;
                }

                UpdateValues(level, now, _currentDrainRate);
            }
            else if (level > _lastBatteryLevel)
            {
                _isInitialized = false;
                UpdateValues(level, now, 0d);
            }
            else
            {
                var elapsed = now - _lastDropTime;
                if (_currentDrainRate > 0d && elapsed > 1.0d)
                {
                    var maxPossibleRate = (1.0d / elapsed) * 3600d;
                    if (maxPossibleRate < _currentDrainRate)
                    {
                        _currentDrainRate = maxPossibleRate;
                    }
                }
            }

            return _currentDrainRate;

            void UpdateValues(float lastBatteryLevel, double dropTime, double drainRate)
            {
                _lastBatteryLevel = lastBatteryLevel;
                _lastDropTime = dropTime;
                _currentDrainRate = drainRate;
            }
        }

        public override void Dispose()
        {
            _isInitialized = false;
            _currentDrainRate = 0d;
            base.Dispose();
        }
    }
}