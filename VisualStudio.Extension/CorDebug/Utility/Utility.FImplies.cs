//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public partial class Utility
    {
        public static bool FImplies(bool b1, bool b2)
        {
            return !b1 || b2;
        }
    }
}
