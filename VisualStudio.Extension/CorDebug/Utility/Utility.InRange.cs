//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public partial class Utility
    {
        public static bool InRange(int i, int iLow, int iHigh)
        {
            return i >= iLow && i <= iHigh;
        }

        public static bool InRange(uint i, uint iLow, uint iHigh)
        {
            return i >= iLow && i <= iHigh;
        }
    }
}
