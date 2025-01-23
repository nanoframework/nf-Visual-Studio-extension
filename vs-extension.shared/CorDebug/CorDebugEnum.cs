//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    //There has got to be a better way than this ugliness...   	
    public class CorDebugEnum : ICorDebugAssemblyEnum,
                                 ICorDebugAppDomainEnum,
                                 ICorDebugErrorInfoEnum,
                                 ICorDebugTypeEnum,
                                 ICorDebugCodeEnum,
                                 ICorDebugValueEnum,
                                 ICorDebugModuleEnum,
                                 ICorDebugChainEnum,
                                 ICorDebugFrameEnum,
                                 ICorDebugThreadEnum,
                                 ICorDebugProcessEnum,
                                 ICorDebugStepperEnum,
                                 ICorDebugObjectEnum,
                                 ICorDebugBreakpointEnum,
                                 IEnumDebugPorts2,
                                 IEnumDebugProcesses2,
                                 IEnumDebugPrograms2
    {
        private object[] m_items;
        private uint m_iCurrent;
        private Type m_typeElement;
        private Type m_typeCollection;

        public CorDebugEnum(ICollection items, Type typeElement, Type typeCollection)
        {
            if (items != null)
            {
                m_items = new Object[items.Count];
                items.CopyTo(m_items, 0);
            }

            m_typeElement = typeElement;
            m_typeCollection = typeCollection;
        }

        public CorDebugEnum(object item, Type typeElement, Type typeCollection) : this (new object[] {item}, typeElement, typeCollection)
        {
        }

        private uint Count
        {
            [System.Diagnostics.DebuggerHidden]
            get { return (uint) (m_items == null ? 0 : m_items.Length); }
        }

        private CorDebugEnum Clone()
        {
            return (CorDebugEnum) MemberwiseClone();
        }

        #region ICorDebugEnum Members

        public int Reset()
        {
            m_iCurrent = 0;
            return COM_HResults.S_OK;
        }

        public int GetCount(out uint pcelt)
        {
            pcelt = Count;
            return COM_HResults.S_OK;
        }

        public int Clone(out IntPtr ppEnum)
        {
            ppEnum = Marshal.GetComInterfaceForObject(Clone(), m_typeCollection);
            return COM_HResults.S_OK;
        }

        public int Skip(uint celt)
        {
            m_iCurrent += celt;
            return COM_HResults.S_OK;
        }

        //Modify the IL to take an array of IntPtrs, no need to allocate temporary memory
        //size is celt
        public int Next(uint celt, System.IntPtr values, out uint pceltFetched)
        {
            pceltFetched = System.Math.Min(celt, Count - m_iCurrent);

#if DEV16
            int[] arr = new int[pceltFetched];
#else
            long[] arr = new long[pceltFetched];
#endif

            for (uint i = 0; i < pceltFetched; i++)
            {
#if DEV16
                arr[i] = Marshal.GetComInterfaceForObject(m_items[(int) m_iCurrent], m_typeElement).ToInt32();
#else
                arr[i] = Marshal.GetComInterfaceForObject(m_items[(int)m_iCurrent], m_typeElement).ToInt64();
#endif
                m_iCurrent++;
            }

            Marshal.Copy(arr, 0, values, (int)pceltFetched);
            return COM_HResults.S_OK;
        }

        #endregion

        public int NextCore(uint celt, object[] ptr, out uint pceltFetched)
        {
            pceltFetched = System.Math.Min(celt, Count - m_iCurrent);

            for (uint i = 0; i < pceltFetched; i++)
            {
                ptr[i] = m_items[(int) m_iCurrent];
                m_iCurrent++;
            }

            return COM_HResults.S_OK;
        }


        public int Clone(out IEnumDebugPorts2 ppEnum)
        {
            ppEnum = (IEnumDebugPorts2) Clone();
            return COM_HResults.S_OK;
        }

        public int Next(uint celt, IDebugPort2[] rgelt, ref uint pceltFetched)
        {
            return NextCore(celt, rgelt, out pceltFetched);
        }

        public int Clone(out IEnumDebugProcesses2 ppEnum)
        {
            ppEnum = (IEnumDebugProcesses2) Clone();
            return COM_HResults.S_OK;
        }

        public int Next(uint celt, IDebugProcess2[] rgelt, ref uint pceltFetched)
        {
            return NextCore(celt, rgelt, out pceltFetched);
        }

        public int Clone(out IEnumDebugPrograms2 ppEnum)
        {
            ppEnum = (IEnumDebugPrograms2) Clone();
            return COM_HResults.S_OK;
        }

        public int Next(uint celt, IDebugProgram2[] rgelt, ref uint pceltFetched)
        {
            return NextCore(celt, rgelt, out pceltFetched);
        }
    }
}

