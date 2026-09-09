using System;
using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class BatteryDrainRateGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Apx. Battery Drain Rate";
        internal override string UnitName => "%/h";

        private float _lastBatteryLevel = -1f;
        private double _lastDropTime;
        private double _currentDrainRate;
        private bool _isInitialized;

        public BatteryDrainRateGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 20.0d,
                expandedByDefault: false,
                register: true
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