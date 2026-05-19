using System;

namespace MemoryPack
{
#if !DEVSUITE_MEMORYPACK
    internal class MemoryPackableAttribute : Attribute
    {
        public MemoryPackableAttribute(GenerateType _)
        {
        }
    }

    internal class MemoryPackOrderAttribute : Attribute
    {
        public MemoryPackOrderAttribute(int _)
        {
        }
    }

    internal class MemoryPackConstructorAttribute : Attribute
    {
    }

    internal enum GenerateType
    {
        VersionTolerant,
    }
#endif
}