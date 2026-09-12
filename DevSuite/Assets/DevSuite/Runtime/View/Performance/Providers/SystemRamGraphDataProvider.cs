using Unity.Profiling;

namespace Ff.DevSuite.Performance
{
    public class SystemRamGraphDataProvider : BaseGraphDataProvider
    {
        internal override string Label => "System RAM";
        internal override string UnitName => "MB";

        private const string DefaultTooltip =
            "Total process system RAM usage in megabytes (MB).\n\n" +
            "Monitored via <b><color=#ffc800>ProfilerRecorder</color></b> for <b><color=#ffc800>ProfilerCategory.Memory</color></b> tracking counter <b><color=#ffc800>\"System Used Memory\"</color></b> (converted via <b><color=#ffc800>CurrentValue / 1024f / 1024f</color></b>).\n\n" +
            "Encompasses total physical memory used by the application, including native engine allocations, loaded meshes, textures, audio buffers, and managed memory.";

        private ProfilerRecorder _profileRecorder;

        public SystemRamGraphDataProvider()
        {
            Settings = new GraphDataProviderSettings(
                referenceValueProvider: () => 2000d,
                expandedByDefault: true,
                register: true,
                tooltip: DefaultTooltip
            );
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