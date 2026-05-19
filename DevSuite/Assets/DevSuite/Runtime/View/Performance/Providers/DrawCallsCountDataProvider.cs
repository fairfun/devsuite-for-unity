using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class DrawCallsCountDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Draw Calls";
        internal override string UnitName => "";

        private ProfilerRecorder _profileRecorder;

        public DrawCallsCountDataProvider()
        {
            ReferenceValueProvider = () => 500;
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.Rendering, true);
#endif
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        }

        protected override double GetCurrentValue()
        {
            return _profileRecorder.Valid ? _profileRecorder.LastValue : 0;
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}