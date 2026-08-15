using Unity.Profiling;
using UnityEngine.Profiling;

namespace Ff.DevSuite.Performance
{
    public class TrianglesCountDataProvider : BaseGraphDataProvider
    {
        public static new bool CollapsedByDefault = true;

        internal override string Label => "Triangles";
        internal override string UnitName => "K";

        private ProfilerRecorder _profileRecorder;

        public TrianglesCountDataProvider()
        {
            ReferenceValueProvider = () => 100d;
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
