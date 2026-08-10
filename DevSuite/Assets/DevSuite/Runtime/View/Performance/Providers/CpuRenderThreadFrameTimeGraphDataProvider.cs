using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class CpuRenderThreadFrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Render Thread Time";
        internal override string UnitName => "ms";

        private ProfilerRecorder _profileRecorderInternal;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public CpuRenderThreadFrameTimeGraphDataProvider()
        {
            ReferenceValueProvider = () => 1000d / DevSuiteUtils.TargetFps;
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
#endif
            _profileRecorderInternal = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
        }

        protected override double GetCurrentValue()
        {
            if (_profileRecorderInternal.Valid && _profileRecorderInternal.LastValue > 0)
            {
                return _profileRecorderInternal.LastValue * 1e-6d;
            }

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0 && _frameTimings[0].cpuRenderThreadFrameTime > 0)
            {
                return _frameTimings[0].cpuRenderThreadFrameTime;
            }

            return 0;
        }

        public override void Dispose()
        {
            _profileRecorderInternal.Dispose();
            base.Dispose();
        }
    }
}