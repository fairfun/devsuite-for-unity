using System;
using Ff.DevSuite.Performance;

namespace Ff.DevSuite
{
    public class DevSuitePerformanceGraphsApi
    {
        private readonly DevSuiteContext _context;

        public DevSuitePerformanceGraphsApi(DevSuiteContext context)
        {
            _context = context;
        }

        public int TicksCapacity
        {
            get => _context.PerformanceGraphTicksCapacity;
            set => _context.PerformanceGraphTicksCapacity = value;
        }

        public int PerformanceGraphTicksCapacity
        {
            get => _context.PerformanceGraphTicksCapacity;
            set => _context.PerformanceGraphTicksCapacity = value;
        }

        public void Register<T>(T provider, GraphDataProviderSettings overrideSettings = null) where T : BaseGraphDataProvider
        {
            _context.RegisterPerformanceGraphInternal(provider, overrideSettings);
        }

        public void RegisterPerformanceGraph<T>(T provider, GraphDataProviderSettings overrideSettings = null) where T : BaseGraphDataProvider
        {
            Register(provider, overrideSettings);
        }

        public void SetSettings<T>(GraphDataProviderSettings settings) where T : BaseGraphDataProvider
        {
            _context.SetPerformanceGraphSettingsInternal<T>(settings);
        }

        public void SetPerformanceGraphSettings<T>(GraphDataProviderSettings settings) where T : BaseGraphDataProvider
        {
            SetSettings<T>(settings);
        }

        public bool IsCollapsed(BaseGraphDataProvider provider)
        {
            return _context.IsPerformanceGraphCollapsed(provider);
        }

        public void SetCollapsed(BaseGraphDataProvider provider, bool collapsed)
        {
            _context.SetPerformanceGraphCollapsed(provider, collapsed);
        }
    }

    [Obsolete("Use DevSuitePerformanceGraphsApi instead. Will be removed in version 1.0.")]
    public class DevSiutePerformanceGraphsApi : DevSuitePerformanceGraphsApi
    {
        public DevSiutePerformanceGraphsApi(DevSuiteContext context) : base(context)
        {
        }
    }
}
