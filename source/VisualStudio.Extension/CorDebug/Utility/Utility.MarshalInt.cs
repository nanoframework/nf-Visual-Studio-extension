//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

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
