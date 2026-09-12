using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class FrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Frame Time";
        internal override string UnitName => "ms";

        private const string DefaultTooltip =
            "Total frame time in milliseconds (ms).\n\n" +
            "Calculated as <b><color=#ffc800>Time.unscaledDeltaTime * 1000f</color></b>. " +
            "Measures the complete duration of the previous frame, including CPU scripts, rendering, GPU execution, and any waiting for VSync or frame rate throttling (<b><color=#ffc800>Application.targetFrameRate</color></b>).\n\n" +
            "Target reference line adapts to <b><color=#ffc800>Application.targetFrameRate</color></b> or display refresh rate.";

        public FrameTimeGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 1f / DevSuiteUtils.TargetFps * 1000f,
                expandedByDefault: true,
                register: true,
                tooltip: DefaultTooltip
            );
        }

        protected override double GetCurrentValue()
        {
            return Time.unscaledDeltaTime * 1000f;
        }
    }
}