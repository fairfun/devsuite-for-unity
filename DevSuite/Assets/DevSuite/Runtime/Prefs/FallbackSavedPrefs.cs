using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ff.Prefs
{
    internal class FallbackSavedPrefs : SavedPrefs
    {
        public FallbackSavedPrefs(string name)
        {
            FilePath = "In Memory";
            Initialize();
        }

        protected override void DoInitialize()
        {
            _data = new TemporarySavedPrefsData();
        }

        protected override Task DoFlush()
        {
            return Task.CompletedTask;
        }
    }

    internal class TemporarySavedPrefsData : ISavedPrefsData
    {
        public Dictionary<string, bool?> Booleans { get; set; } = new();
        public Dictionary<string, int?> Integers { get; set; } = new();
        public Dictionary<string, float?> Floats { get; set; } = new();
        public Dictionary<string, string> Strings { get; set; } = new();
        public Dictionary<string, byte[]> Objects { get; set; } = new();

        public void Clear()
        {
            Booleans.Clear();
            Integers.Clear();
            Floats.Clear();
            Strings.Clear();
            Objects.Clear();
        }
    }
}