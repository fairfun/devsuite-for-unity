using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class CpuRenderThreadFrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Render Thread Time";
        internal override string UnitName => "ms";

        private const string DefaultTooltip =
            "CPU render thread time in milliseconds (ms).\n\n" +
            "Captured via <b><color=#ffc800>FrameTimingManager.GetLatestTimings()</color></b> reading <b><color=#ffc800>FrameTiming.cpuRenderThreadFrameTime</color></b>. " +
            "If unavailable, falls back to <b><color=#ffc800>ProfilerRecorder</color></b> for <b><color=#ffc800>ProfilerCategory.Internal</color></b> counter <b><color=#ffc800>\"CPU Render Thread Frame Time\"</color></b> (converted via <b><color=#ffc800>LastValue * 1e-6d</color></b>).\n\n" +
            "Only active when multithreaded rendering is enabled in Player Settings.";

        private ProfilerRecorder _profileRecorderInternal;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public CpuRenderThreadFrameTimeGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 1000d / DevSuiteUtils.TargetFps,
                expandedByDefault: false,
                register: true,
                tooltip: DefaultTooltip
            );
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
#endif
            _profileRecorderInternal = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
        }

        protected override double GetCurrentValue()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0 && _frameTimings[0].cpuRenderThreadFrameTime > 0)
            {
                return _frameTimings[0].cpuRenderThreadFrameTime;
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