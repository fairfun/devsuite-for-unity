using MemoryPack;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Ff.Prefs
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    [MessagePackObject(AllowPrivate = true)]
    [Serializable][DataContract]
    internal partial class DefaultSavedPrefsData : ISavedPrefsData
    {
        [DataMember, MemoryPackOrder(0), Key(0)] public Dictionary<string, bool?> Booleans { get; set; } = new();
        [DataMember, MemoryPackOrder(1), Key(1)] public Dictionary<string, int?> Integers { get; set; } = new();
        [DataMember, MemoryPackOrder(2), Key(2)] public Dictionary<string, float?> Floats { get; set; } = new();
        [DataMember, MemoryPackOrder(3), Key(3)] public Dictionary<string, string> Strings { get; set; } = new();
        [DataMember, MemoryPackOrder(4), Key(4)] public Dictionary<string, byte[]> Objects { get; set; } = new();

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