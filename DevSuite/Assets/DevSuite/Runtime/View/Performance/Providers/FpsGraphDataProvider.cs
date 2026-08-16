using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class FpsGraphDataProvider : BaseGraphDataProvider
    {
        public override float? ReferenceValueColorImpact => -1.5f;

        internal override string Label => "FPS";
        internal override string UnitName => "fps";

        public FpsGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => (double)DevSuiteUtils.TargetFps,
                expandedByDefault: false,
                register: true
            );
        }

        protected override double GetCurrentValue()
        {
            var dt = Time.unscaledDeltaTime;
            return dt > 0f ? 1f / dt : 0d;
        }
    }
}
