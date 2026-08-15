using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class GpuFrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        public static new bool CollapsedByDefault = true;

        internal override string Label => "GPU Frame Time";
        internal override string UnitName => "ms";

        private ProfilerRecorder _profileRecorderInternal;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public GpuFrameTimeGraphDataProvider()
        {
            ReferenceValueProvider = () => 1000d / DevSuiteUtils.TargetFps;
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, true);
#endif
            _profileRecorderInternal = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");
        }

        protected override double GetCurrentValue()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0 && _frameTimings[0].gpuFrameTime > 0)
            {
                return _frameTimings[0].gpuFrameTime;
            }

            if (_profileRecorderInternal.Valid && _profileRecorderInternal.LastValue > 0)
            {
                return _profileRecorderInternal.LastValue * 1e-6d;
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