using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class DrawCallsCountDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Draw Calls";
        internal override string UnitName => "";

        private const string DefaultTooltip =
            "Number of draw calls per frame.\n\n" +
            "Recorded via <b><color=#ffc800>ProfilerRecorder</color></b> for <b><color=#ffc800>ProfilerCategory.Render</color></b> counter <b><color=#ffc800>\"Draw Calls Count\"</color></b> using <b><color=#ffc800>LastValue</color></b>. " +
            "Automatically enables <b><color=#ffc800>ProfilerArea.Rendering</color></b> in the Unity Editor.\n\n" +
            "Represents individual rendering commands sent to the GPU. High draw call counts increase CPU overhead.";

        private ProfilerRecorder _profileRecorder;

        public DrawCallsCountDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 500d,
                expandedByDefault: true,
                register: true,
                tooltip: DefaultTooltip
            );
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.Rendering, true);
#endif
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        }

        protected override double GetCurrentValue()
        {
            return _profileRecorder.Valid ? _profileRecorder.LastValue : 0;
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}