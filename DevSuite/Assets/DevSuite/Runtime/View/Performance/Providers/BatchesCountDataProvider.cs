using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class BatchesCountDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Batches";
        internal override string UnitName => "";

        private ProfilerRecorder _profileRecorder;

        public BatchesCountDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 500d,
                expandedByDefault: false,
                register: true
            );
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
