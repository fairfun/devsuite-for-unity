using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class FpsGraphDataProvider : BaseGraphDataProvider
    {
        public override float? ReferenceValueColorImpact => -1.5f;

        internal override string Label => "FPS";
        internal override string UnitName => "fps";

        private const string DefaultTooltip =
            "Frames per second (FPS).\n\n" +
            "Calculated as <b><color=#ffc800>1f / Time.unscaledDeltaTime</color></b>. " +
            "Shows the raw instantaneous frame rate without multi-frame smoothing.\n\n" +
            "Uses an inverted color gradient where higher framerates are green and drops below the reference target are penalized.";

        public FpsGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => (double)DevSuiteUtils.TargetFps,
                expandedByDefault: false,
                register: true,
                tooltip: DefaultTooltip
            );
        }

        protected override double GetCurrentValue()
        {
            var dt = Time.unscaledDeltaTime;
            return dt > 0f ? 1f / dt : 0d;
        }
    }
}
