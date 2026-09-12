using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class BatchesCountDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Batches";
        internal override string UnitName => "";

        private const string DefaultTooltip =
            "Number of rendered batches per frame.\n\n" +
            "Recorded via <b><color=#ffc800>ProfilerRecorder</color></b> for <b><color=#ffc800>ProfilerCategory.Render</color></b> counter <b><color=#ffc800>\"Batches Count\"</color></b> using <b><color=#ffc800>LastValue</color></b>. " +
            "Automatically enables <b><color=#ffc800>ProfilerArea.Rendering</color></b> in the Unity Editor.\n\n" +
            "Combines multiple draw calls sharing material state through static batching, dynamic batching, GPU instancing, or the SRP Batcher.";

        private ProfilerRecorder _profileRecorder;

        public BatchesCountDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 500d,
                expandedByDefault: false,
                register: true,
                tooltip: DefaultTooltip
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
