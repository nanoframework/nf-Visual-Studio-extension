using System;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public partial class Utility
    {
        public static void MarshalInt(IntPtr ptr, int val)
        {
            if (ptr != IntPtr.Zero)
                Marshal.WriteInt32(ptr, val);
        }
    }
}
