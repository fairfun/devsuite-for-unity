using UnityEngine;

namespace Ff.DevSuite.Performance
{
    public class FrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Frame Time";
        internal override string UnitName => "ms";

        public FrameTimeGraphDataProvider()
        {
            ReferenceValueProvider = () => 1f / DevSuiteUtils.TargetFps * 1000f;
        }

        protected override double GetCurrentValue()
        {
            return Time.unscaledDeltaTime * 1000f;
        }
    }
}