using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class CpuFrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "CPU Frame Time";
        internal override string UnitName => "ms";

        private ProfilerRecorder _profileRecorder;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public CpuFrameTimeGraphDataProvider()
        {
            ReferenceValueProvider = () => 1000d / DevSuiteUtils.TargetFps;
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
#endif
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Total Frame Time");
        }

        protected override double GetCurrentValue()
        {
            if (_profileRecorder.Valid && _profileRecorder.LastValue > 0)
            {
                return _profileRecorder.LastValue * 1e-6d;
            }

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0 && _frameTimings[0].cpuFrameTime > 0)
            {
                return _frameTimings[0].cpuFrameTime;
            }

            return 0;
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}