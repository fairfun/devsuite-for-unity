using System;

namespace MessagePack
{
#if !DEVSUITE_MESSAGEPACK
    internal class MessagePackObjectAttribute : Attribute
    {
        public bool AllowPrivate;
    }

    internal class KeyAttribute : Attribute
    {
        public KeyAttribute(int _)
        {
        }
    }

    internal class IgnoreMemberAttribute : Attribute
    {
    }
#endif
}