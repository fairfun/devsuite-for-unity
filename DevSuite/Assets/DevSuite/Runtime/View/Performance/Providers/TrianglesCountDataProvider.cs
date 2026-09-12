using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class TrianglesCountDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "Triangles";
        internal override string UnitName => "K";

        private const string DefaultTooltip =
            "Rendered triangles count in thousands (K) per frame.\n\n" +
            "Recorded via <b><color=#ffc800>ProfilerRecorder</color></b> for <b><color=#ffc800>ProfilerCategory.Render</color></b> counter <b><color=#ffc800>\"Triangles Count\"</color></b> (converted via <b><color=#ffc800>LastValue / 1000d</color></b>). " +
            "Automatically enables <b><color=#ffc800>ProfilerArea.Rendering</color></b> in the Unity Editor.\n\n" +
            "High polygon counts impact vertex processing and rasterization performance.";

        private ProfilerRecorder _profileRecorder;

        public TrianglesCountDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 100d,
                expandedByDefault: false,
                register: true,
                tooltip: DefaultTooltip
            );
#if UNITY_EDITOR
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.Rendering, true);
#endif
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        protected override double GetCurrentValue()
        {
            return _profileRecorder.Valid ? _profileRecorder.LastValue / 1000d : 0d;
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}
