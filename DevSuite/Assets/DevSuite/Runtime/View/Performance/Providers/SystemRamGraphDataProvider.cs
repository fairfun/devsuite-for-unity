using Unity.Profiling;

namespace Ff.DevSuite.Performance
{
    public class SystemRamGraphDataProvider : BaseGraphDataProvider
    {

        internal override string Label => "System RAM";
        internal override string UnitName => "MB";

        private ProfilerRecorder _profileRecorder;

        public SystemRamGraphDataProvider()
        {
            ReferenceValueProvider = () => 2000d;
            _profileRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        }

        protected override double GetCurrentValue()
        {
            return _profileRecorder.CurrentValue / 1024f / 1024f;
        }

        public override void Dispose()
        {
            _profileRecorder.Dispose();
            base.Dispose();
        }
    }
}