using System;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class GcMemoryGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "GC Memory";
        internal override string UnitName => "MB";

        private const string DefaultTooltip =
            "GC allocated memory in megabytes (MB).\n\n" +
            "Recorded via <b><color=#ffc800>ProfilerRecorder</color></b> for <b><color=#ffc800>ProfilerCategory.Memory</color></b> counter <b><color=#ffc800>\"GC Used Memory\"</color></b> (converted via <b><color=#ffc800>LastValue / (1024d * 1024d)</color></b>). " +
            "If the recorder is unavailable, falls back to <b><color=#ffc800>GC.GetTotalMemory(false) / (1024d * 1024d)</color></b>.\n\n" +
            "Measures managed heap memory. Rapid growth indicates per-frame allocations that will trigger garbage collection pauses.";

        private ProfilerRecorder _profileRecorder;

        public GcMemoryGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 100d,
                expandedByDefault: false,
                register: true,
                tooltip: DefaultTooltip
            );
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
#endif
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
        }

        protected override double GetCurrentValue()
        {
            if (_profileRecorder.Valid && _profileRecorder.LastValue > 0)
            {
                return _profileRecorder.LastValue / (1024d * 1024d);
            }
            return GC.GetTotalMemory(false) / (1024d * 1024d);
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}
