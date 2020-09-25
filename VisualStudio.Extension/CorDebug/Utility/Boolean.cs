//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class Boolean
    {
        public const int FALSE = 0;
        public const int TRUE = 1;
        public static int BoolToInt(bool b) { return b ? TRUE : FALSE; }
        public static bool IntToBool(int i) { return i == 0 ? false : true; }
    }
}
