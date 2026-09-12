using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class CpuFrameTimeGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "CPU Frame Time";
        internal override string UnitName => "ms";

        private const string DefaultTooltip =
            "Total CPU frame time in milliseconds (ms).\n\n" +
            "Obtained via <b><color=#ffc800>FrameTimingManager.GetLatestTimings()</color></b> reading <b><color=#ffc800>FrameTiming.cpuFrameTime</color></b>. " +
            "If Frame Timing Stats are unavailable, falls back to <b><color=#ffc800>ProfilerRecorder</color></b> tracking <b><color=#ffc800>ProfilerCategory.Internal</color></b> counter <b><color=#ffc800>\"CPU Total Frame Time\"</color></b> (converted via <b><color=#ffc800>LastValue * 1e-6d</color></b>).\n\n" +
            "Enable <b><color=#ffc800>Frame Timing Stats</color></b> in Player Settings for hardware-accurate timings.";

        private ProfilerRecorder _profileRecorder;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public CpuFrameTimeGraphDataProvider()
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
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Total Frame Time");
        }

        protected override double GetCurrentValue()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0 && _frameTimings[0].cpuFrameTime > 0)
            {
                return _frameTimings[0].cpuFrameTime;
            }

            if (_profileRecorder.Valid && _profileRecorder.LastValue > 0)
            {
                return _profileRecorder.LastValue * 1e-6d;
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