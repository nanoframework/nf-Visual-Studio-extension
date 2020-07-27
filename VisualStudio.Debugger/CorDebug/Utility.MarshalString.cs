using System;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Debugger
{
    public partial class Utility
    {
        public static void MarshalString(string s, uint cchName, IntPtr pcchName, System.IntPtr szName, bool fIncludeNullTerminator)
        {
            if (s == null)
                s = "";

            if (fIncludeNullTerminator)
                s += '\0';

            int cch = s.Length;

            if (szName != IntPtr.Zero)
            {
                char[] chars = s.ToCharArray();

                cch = System.Math.Min((int)cchName, cch);
                if (fIncludeNullTerminator)
                    chars[cch - 1] = '\0';

                Marshal.Copy(chars, 0, szName, cch);
            }

            if (pcchName != IntPtr.Zero)
                Marshal.WriteInt32(pcchName, cch);
        }

        public static void MarshalString(string s, uint cchName, IntPtr pcchName, System.IntPtr szName)
        {
            MarshalString(s, cchName, pcchName, szName, true);
        }
    }
}
