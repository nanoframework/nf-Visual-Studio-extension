//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WireProtocol = nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugFrame : ICorDebugFrame, ICorDebugILFrame, ICorDebugNativeFrame, ICorDebugILFrame2//
    {
        const uint STACK_BEGIN = uint.MaxValue;
        const uint IP_NOT_INITIALIZED = uint.MaxValue;
        public const uint DEPTH_CLR_INVALID = uint.MaxValue;

        CorDebugChain _chain;
        CorDebugFunction _function;
        uint _IP;
        uint _depthnanoCLR;
        internal uint _depthCLR;
        WireProtocol.Commands.Debugging_Thread_Stack.Reply.Call _call;

        public CorDebugFrame(CorDebugChain chain, WireProtocol.Commands.Debugging_Thread_Stack.Reply.Call call, uint depth)
        {
            _chain = chain;
            _depthnanoCLR = depth;
            _call = call;
            _IP = IP_NOT_INITIALIZED;
        }

        public ICorDebugFrame ICorDebugFrame
        {
            get { return (ICorDebugFrame)this; }
        }

        public ICorDebugILFrame ICorDebugILFrame
        {
            get { return (ICorDebugILFrame)this; }
        }

        public CorDebugFrame Clone()
        {
            return (CorDebugFrame)MemberwiseClone();
        }

        public CorDebugProcess Process
        {
            [DebuggerHidden]
            get { return Chain.Thread.Process; }
        }

        public CorDebugAppDomain AppDomain
        {
            get { return Function.AppDomain; }
        }

        public Engine Engine
        {
            [DebuggerHidden]
            get { return Process.Engine; }
        }

        public CorDebugChain Chain
        {
            [DebuggerHidden]
            get { return _chain; }
        }

        public CorDebugThread Thread
        {
            [DebuggerHidden]
            get { return _chain.Thread; }
        }

        public uint DepthCLR
        {
            [DebuggerHidden]
            get { return _depthCLR; }
        }

        public uint DepthnanoCLR
        {
            [DebuggerHidden]
            get { return _depthnanoCLR; }
        }

        public static uint AppDomainIdFromCall(Engine engine, WireProtocol.Commands.Debugging_Thread_Stack.Reply.Call call)
        {
            uint appDomainId = CorDebugAppDomain.c_AppDomainId_ForNoAppDomainSupport;

            if (engine.Capabilities.AppDomains)
            {
                WireProtocol.Commands.Debugging_Thread_Stack.Reply.CallEx callEx = call as WireProtocol.Commands.Debugging_Thread_Stack.Reply.CallEx;

                appDomainId = callEx.m_appDomainID;
            }

            return appDomainId;
        }

        public virtual CorDebugFunction Function
        {
            get
            {
                if (_function == null)
                {
                    uint appDomainId = AppDomainIdFromCall(Engine, _call);

                    CorDebugAppDomain appDomain = Process.GetAppDomainFromId(appDomainId);
                    CorDebugAssembly assembly = appDomain.AssemblyFromIndex(_call.m_md); ;

                    uint tkMethod = nanoCLR_TypeSystem.nanoCLRTokenFromMethodIndex(_call.m_md);

                    _function = assembly.GetFunctionFromTokennanoCLR(tkMethod);
                }

                return _function;
            }
        }

        public uint IP
        {
            get
            {
                if (_IP == IP_NOT_INITIALIZED)
                {
                    _IP = Function.HasSymbols ? Function.GetILCLRFromILnanoCLR(_call.m_IP) : _call.m_IP;
                }

                return _IP;
            }
        }

        public uint IP_nanoCLR
        {
            [DebuggerHidden]
            get { return _call.m_IP; }
        }

        private CorDebugValue GetStackFrameValue(uint dwIndex, Engine.StackValueKind kind)
        {
            var stackFrameValue = Engine.GetStackFrameValue(_chain.Thread.ID, _depthnanoCLR, kind, dwIndex);

            return CorDebugValue.CreateValue(stackFrameValue, AppDomain);
        }

        public uint Flags
        {
            get
            {
                WireProtocol.Commands.Debugging_Thread_Stack.Reply.CallEx callEx = _call as WireProtocol.Commands.Debugging_Thread_Stack.Reply.CallEx;
                return (callEx == null) ? 0 : callEx.m_flags;
            }
        }

        public static void GetStackRange(CorDebugThread thread, uint depthCLR, out ulong start, out ulong end)
        {
            for (CorDebugThread threadT = thread.GetRealCorDebugThread(); threadT != thread; threadT = threadT.NextThread)
            {
                Debug.Assert(threadT.IsSuspended);
                depthCLR += threadT.Chain.NumFrames;
            }

            start = depthCLR;
            end = start;
        }

        #region ICorDebugFrame Members

        int ICorDebugFrame.GetChain(out ICorDebugChain ppChain)
        {
            ppChain = (ICorDebugChain)_chain;

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.GetCaller(out ICorDebugFrame ppFrame)
        {
            ppFrame = (ICorDebugFrame)_chain.GetFrameFromDepthCLR(_depthCLR - 1);

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.GetFunctionToken(out uint pToken)
        {
            Function.ICorDebugFunction.GetToken(out pToken);

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.GetCallee(out ICorDebugFrame ppFrame)
        {
            ppFrame = (ICorDebugFrame)_chain.GetFrameFromDepthCLR(_depthCLR + 1);

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.GetCode(out ICorDebugCode ppCode)
        {
            ppCode = new CorDebugCode(Function);

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.GetFunction(out ICorDebugFunction ppFunction)
        {
            ppFunction = Function;

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.CreateStepper(out ICorDebugStepper ppStepper)
        {
            ppStepper = new CorDebugStepper(this);

            return COM_HResults.S_OK;
        }

        int ICorDebugFrame.GetStackRange(out ulong pStart, out ulong pEnd)
        {
            GetStackRange(Thread, _depthCLR, out pStart, out pEnd);

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugILFrame Members

        int ICorDebugILFrame.GetChain(out ICorDebugChain ppChain)
        {
            return ((ICorDebugFrame)this).GetChain(out ppChain);
        }

        int ICorDebugILFrame.GetCode(out ICorDebugCode ppCode)
        {
            return ((ICorDebugFrame)this).GetCode(out ppCode);
        }

        int ICorDebugILFrame.GetFunction(out ICorDebugFunction ppFunction)
        {
            return ((ICorDebugFrame)this).GetFunction(out ppFunction);
        }

        int ICorDebugILFrame.GetFunctionToken(out uint pToken)
        {
            return ((ICorDebugFrame)this).GetFunctionToken(out pToken);
        }

        int ICorDebugILFrame.GetStackRange(out ulong pStart, out ulong pEnd)
        {
            return ((ICorDebugFrame)this).GetStackRange(out pStart, out pEnd);
        }

        int ICorDebugILFrame.GetCaller(out ICorDebugFrame ppFrame)
        {
            return ((ICorDebugFrame)this).GetCaller(out ppFrame);
        }

        int ICorDebugILFrame.GetCallee(out ICorDebugFrame ppFrame)
        {
            return ((ICorDebugFrame)this).GetCallee(out ppFrame);
        }

        int ICorDebugILFrame.CreateStepper(out ICorDebugStepper ppStepper)
        {
            return ((ICorDebugFrame)this).CreateStepper(out ppStepper);
        }

        int ICorDebugILFrame.GetIP(out uint pnOffset, out CorDebugMappingResult pMappingResult)
        {
            pnOffset = IP;
            pMappingResult = CorDebugMappingResult.MAPPING_EXACT;

            return COM_HResults.S_OK;
        }

        int ICorDebugILFrame.SetIP(uint nOffset)
        {
            uint ip = Function.GetILnanoCLRFromILCLR(nOffset);

            if (Engine.SetIPOfStackFrame(Thread.ID, _depthnanoCLR, ip, 0/*compute eval depth*/))
            {
                _call.m_IP = ip;
                _IP = nOffset;
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugILFrame.EnumerateLocalVariables(out ICorDebugValueEnum ppValueEnum)
        {
            ppValueEnum = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugILFrame.GetLocalVariable(uint dwIndex, out ICorDebugValue ppValue)
        {
            ppValue = GetStackFrameValue(dwIndex, Engine.StackValueKind.Local);

            return COM_HResults.S_OK;
        }

        int ICorDebugILFrame.EnumerateArguments(out ICorDebugValueEnum ppValueEnum)
        {
            ppValueEnum = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugILFrame.GetArgument(uint dwIndex, out ICorDebugValue ppValue)
        {
            ppValue = GetStackFrameValue(dwIndex, Engine.StackValueKind.Argument);

            return COM_HResults.S_OK;
        }

        int ICorDebugILFrame.GetStackDepth(out uint pDepth)
        {
            pDepth = _depthCLR;

            return COM_HResults.S_OK;
        }

        int ICorDebugILFrame.GetStackValue(uint dwIndex, out ICorDebugValue ppValue)
        {
            ppValue = GetStackFrameValue(dwIndex, Engine.StackValueKind.EvalStack);
            Debug.Assert(false, "Not tested");

            return COM_HResults.S_OK;
        }

        int ICorDebugILFrame.CanSetIP(uint nOffset)
        {
            //IF WE DON"T ENSURE THAT THE IP is VALID....we are hosed
            //Not in an Exception block, at zero eval stack....etc....

            return COM_HResults.S_OK;
        }

        #endregion


        #region ICorDebugILFrame2 Members

        int ICorDebugILFrame2.RemapFunction([In] uint newILOffset)
        {
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugILFrame2.EnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum)
        {
            ppTyParEnum = null;
            return COM_HResults.E_NOTIMPL;
        }

        #endregion

        //ICorDebugNative is needed for CPDE to back the IP up to the beginning
        //of a sequence point when the user intercepts an exception.
        #region ICorDebugNativeFrame Members

        int ICorDebugNativeFrame.GetChain(out ICorDebugChain ppChain)
        {
            return ((ICorDebugFrame)this).GetChain(out ppChain);
        }

        int ICorDebugNativeFrame.GetCode(out ICorDebugCode ppCode)
        {
            return ((ICorDebugFrame)this).GetCode(out ppCode);
        }

        int ICorDebugNativeFrame.GetFunction(out ICorDebugFunction ppFunction)
        {
            return ((ICorDebugFrame)this).GetFunction(out ppFunction);
        }

        int ICorDebugNativeFrame.GetFunctionToken(out uint pToken)
        {
            return ((ICorDebugFrame)this).GetFunctionToken(out pToken);
        }

        int ICorDebugNativeFrame.GetStackRange(out ulong pStart, out ulong pEnd)
        {
            return ((ICorDebugFrame)this).GetStackRange(out pStart, out pEnd);
        }

        int ICorDebugNativeFrame.GetCaller(out ICorDebugFrame ppFrame)
        {
            return ((ICorDebugFrame)this).GetCaller(out ppFrame);
        }

        int ICorDebugNativeFrame.GetCallee(out ICorDebugFrame ppFrame)
        {
            return ((ICorDebugFrame)this).GetCallee(out ppFrame);
        }

        int ICorDebugNativeFrame.CreateStepper(out ICorDebugStepper ppStepper)
        {
            return ((ICorDebugFrame)this).CreateStepper(out ppStepper);
        }

        int ICorDebugNativeFrame.GetIP(out uint pnOffset)
        {
            CorDebugMappingResult ignorable;
            return ((ICorDebugILFrame)this).GetIP(out pnOffset, out ignorable);
        }

        int ICorDebugNativeFrame.SetIP(uint nOffset)
        {
            return ((ICorDebugILFrame)this).SetIP(nOffset);
        }

        int ICorDebugNativeFrame.GetRegisterSet(out ICorDebugRegisterSet ppRegisters)
        {
            Debug.Assert(false);
            ppRegisters = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugNativeFrame.GetLocalRegisterValue(CorDebugRegister reg, uint cbSigBlob, IntPtr pvSigBlob, out ICorDebugValue ppValue)
        {
            Debug.Assert(false);
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugNativeFrame.GetLocalDoubleRegisterValue(CorDebugRegister highWordReg, CorDebugRegister lowWordReg, uint cbSigBlob, IntPtr pvSigBlob, out ICorDebugValue ppValue)
        {
            Debug.Assert(false);
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugNativeFrame.GetLocalMemoryValue(ulong address, uint cbSigBlob, IntPtr pvSigBlob, out ICorDebugValue ppValue)
        {
            Debug.Assert(false);
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugNativeFrame.GetLocalRegisterMemoryValue(CorDebugRegister highWordReg, ulong lowWordAddress, uint cbSigBlob, IntPtr pvSigBlob, out ICorDebugValue ppValue)
        {
            Debug.Assert(false);
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugNativeFrame.GetLocalMemoryRegisterValue(ulong highWordAddress, CorDebugRegister lowWordRegister, uint cbSigBlob, IntPtr pvSigBlob, out ICorDebugValue ppValue)
        {
            Debug.Assert(false);
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugNativeFrame.CanSetIP(uint nOffset)
        {
            return ((ICorDebugILFrame)this).CanSetIP(nOffset);
        }

        #endregion
    }

    public class CorDebugInternalFrame : CorDebugFrame, ICorDebugInternalFrame
    {
        CorDebugInternalFrameType _type;

        public CorDebugInternalFrame(CorDebugChain chain, CorDebugInternalFrameType type)
            : base(chain, null, CorDebugFrame.DEPTH_CLR_INVALID)
        {
            _type = type;
        }

        public CorDebugInternalFrameType FrameType
        {
            get { return _type; }
        }

        public override CorDebugFunction Function
        {
            get { return null; }
        }

        #region ICorDebugInternalFrame Members

        int ICorDebugInternalFrame.GetChain(out ICorDebugChain ppChain)
        {
            return ICorDebugFrame.GetChain(out ppChain);
        }

        int ICorDebugInternalFrame.GetCode(out ICorDebugCode ppCode)
        {
            return ICorDebugFrame.GetCode(out ppCode);
        }

        int ICorDebugInternalFrame.GetFunction(out ICorDebugFunction ppFunction)
        {
            return ICorDebugFrame.GetFunction(out ppFunction);
        }

        int ICorDebugInternalFrame.GetFunctionToken(out uint pToken)
        {
            return ICorDebugFrame.GetFunctionToken(out pToken);
        }

        int ICorDebugInternalFrame.GetStackRange(out ulong pStart, out ulong pEnd)
        {
            return ICorDebugFrame.GetStackRange(out pStart, out pEnd);
        }

        int ICorDebugInternalFrame.GetCaller(out ICorDebugFrame ppFrame)
        {
            return ICorDebugFrame.GetCaller(out ppFrame);
        }

        int ICorDebugInternalFrame.GetCallee(out ICorDebugFrame ppFrame)
        {
            return ICorDebugFrame.GetCallee(out ppFrame);
        }

        int ICorDebugInternalFrame.CreateStepper(out ICorDebugStepper ppStepper)
        {
            return ICorDebugFrame.CreateStepper(out ppStepper);
        }

        int ICorDebugInternalFrame.GetFrameType(out CorDebugInternalFrameType pType)
        {
            pType = _type;

            return COM_HResults.S_OK;
        }

        #endregion
    }
}
