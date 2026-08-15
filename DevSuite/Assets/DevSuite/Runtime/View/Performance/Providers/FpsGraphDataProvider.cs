using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class FpsGraphDataProvider : BaseGraphDataProvider
    {
        public static new bool CollapsedByDefault = true;
        public override float? ReferenceValueColorImpact => -1.5f;

        internal override string Label => "FPS";
        internal override string UnitName => "fps";

        public FpsGraphDataProvider()
        {
            ReferenceValueProvider = () => (double)DevSuiteUtils.TargetFps;
        }

        protected override double GetCurrentValue()
        {
            var dt = Time.unscaledDeltaTime;
            return dt > 0f ? 1f / dt : 0d;
        }
    }
}
