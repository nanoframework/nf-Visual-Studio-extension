//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Debugger.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class DebugEvent : IDebugEvent2
    {
        private uint m_attributes;

        public DebugEvent(uint attributes)
        {
            m_attributes = attributes;
        }

        #region IDebugEvent2 Members

        public int GetAttributes(out uint pdwAttrib)
        {
            pdwAttrib = m_attributes;
            return COM_HResults.S_OK;
        }

        #endregion

    }

}
