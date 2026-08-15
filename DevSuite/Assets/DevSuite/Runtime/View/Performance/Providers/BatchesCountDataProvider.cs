using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class BatchesCountDataProvider : BaseGraphDataProvider
    {
        public static bool RegisterByDefault = true;
        public static new bool CollapsedByDefault = true;

        internal override string Label => "Batches";
        internal override string UnitName => "";

        private ProfilerRecorder _profileRecorder;

        public BatchesCountDataProvider()
        {
            ReferenceValueProvider = () => 500d;
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.Rendering, true);
#endif
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        }

        protected override double GetCurrentValue()
        {
            return _profileRecorder.Valid ? _profileRecorder.LastValue : 0d;
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}
