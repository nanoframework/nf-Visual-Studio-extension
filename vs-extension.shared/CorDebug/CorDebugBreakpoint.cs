//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using BreakpointDef = nanoFramework.Tools.Debugger.WireProtocol.Commands.Debugging_Execution_BreakpointDef;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public abstract class CorDebugBreakpoint : CorDebugBreakpointBase, ICorDebugBreakpoint
    {
        public CorDebugBreakpoint(CorDebugAppDomain appDomain) : base(appDomain)
        {
            Kind = BreakpointDef.c_HARD;
        }

        protected abstract Type TypeToMarshal
        {
            get;
        }

        public override void Hit(BreakpointDef breakpointDef)
        {
            CorDebugThread thread = Process.GetThread(breakpointDef.m_pid);

            Process.EnqueueEvent(new ManagedCallbacks.ManagedCallbackBreakpoint(thread, this, TypeToMarshal));
        }

        #region ICorDebugBreakpoint Members

        int ICorDebugBreakpoint.Activate(int bActive)
        {
            Active = Boolean.IntToBool(bActive);

            return COM_HResults.S_OK;
        }

        int ICorDebugBreakpoint.IsActive(out int pbActive)
        {
            pbActive = Boolean.BoolToInt(Active);

            return COM_HResults.S_OK;
        }

        #endregion
    }

    public class CorDebugFunctionBreakpoint : CorDebugBreakpoint, ICorDebugFunctionBreakpoint
    {
        private CorDebugFunction m_function;
        private IL m_il;

        public CorDebugFunctionBreakpoint(CorDebugFunction function, uint ilCLR) : base(function.AppDomain)
        {
            m_function = function;
            m_il = new IL();
            m_il.CLRToken = ilCLR;
            m_il.NanoCLRToken = function.GetILnanoCLRFromILCLR(ilCLR);

            m_breakpointDef.m_IP = m_il.NanoCLRToken;
            m_breakpointDef.m_md = m_function.MethodDef_Index;

            Active = true;
        }

        protected override Type TypeToMarshal
        {
            [System.Diagnostics.DebuggerHidden]
            get { return typeof(ICorDebugFunctionBreakpoint); }
        }

        /*
            Function breakpoints are a bit special.  In order not to burden the nanoCLR with duplicate function
            breakpoints for each AppDomain. 
        */
        public override bool Equals(CorDebugBreakpointBase breakpoint)
        {
            CorDebugFunctionBreakpoint bp = breakpoint as CorDebugFunctionBreakpoint;

            if (bp == null) return false;

            if (m_breakpointDef.m_IP != bp.m_breakpointDef.m_IP) return false;
            if (m_breakpointDef.m_md != bp.m_breakpointDef.m_md) return false;

            return true;
        }

        public override bool IsMatch(Commands.Debugging_Execution_BreakpointDef breakpointDef)
        {
            if (breakpointDef.m_flags != BreakpointDef.c_HARD) return false;
            if (breakpointDef.m_IP != m_breakpointDef.m_IP) return false;
            if (breakpointDef.m_md != m_breakpointDef.m_md) return false;

            CorDebugThread thread = m_function.Process.GetThread(breakpointDef.m_pid);
            CorDebugFrame frame = thread.Chain.ActiveFrame;
            if (frame.AppDomain != m_function.AppDomain) return false;

            return true;
        }

        public CorDebugFunction Function
        {
            [System.Diagnostics.DebuggerHidden]
            get { return m_function; }
        }

        #region ICorDebugBreakpoint Members

        int ICorDebugBreakpoint.Activate(int bActive)
        {
            Active = Boolean.IntToBool(bActive);

            return COM_HResults.S_OK;
        }

        int ICorDebugBreakpoint.IsActive(out int pbActive)
        {
            pbActive = Boolean.BoolToInt(Active);

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugFunctionBreakpoint Members

        int ICorDebugFunctionBreakpoint.Activate(int bActive)
        {
            return ((ICorDebugBreakpoint)this).Activate(bActive);
        }

        int ICorDebugFunctionBreakpoint.IsActive(out int pbActive)
        {
            return ((ICorDebugBreakpoint)this).IsActive(out pbActive);
        }

        int ICorDebugFunctionBreakpoint.GetFunction(out ICorDebugFunction ppFunction)
        {
            ppFunction = m_function;

            return COM_HResults.S_OK;
        }

        int ICorDebugFunctionBreakpoint.GetOffset(out uint pnOffset)
        {
            pnOffset = m_il.CLRToken;

            return COM_HResults.S_OK;
        }

        #endregion
    }
}
