using System;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class GcMemoryGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "GC Memory";
        internal override string UnitName => "MB";

        private ProfilerRecorder _profileRecorder;

        public GcMemoryGraphDataProvider()
        {
            ReferenceValueProvider = () => 100d;
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
