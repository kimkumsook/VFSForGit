using System;

namespace PrjFSLib.POSIX
{
    [Flags]
    public enum UpdateType
    {
        Invalid         = 0x00000000,

        AllowReadOnly   = 0x00000020,
    }
}
