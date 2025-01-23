//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System.Collections;
using System.Diagnostics;
using WireProtocol = nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugChain : ICorDebugChain
    {
        CorDebugThread  m_thread;
        CorDebugFrame[] m_frames;
        
        public CorDebugChain(CorDebugThread thread, WireProtocol.Commands.Debugging_Thread_Stack.Reply.Call [] calls)
        {
            m_thread = thread;

            ArrayList frames = new ArrayList(calls.Length);
            bool lastFrameWasUnmanaged = false;

            if (thread.IsVirtualThread)
            {
                frames.Add(new CorDebugInternalFrame(this, CorDebugInternalFrameType.STUBFRAME_FUNC_EVAL));
            }

            for (uint i = 0; i < calls.Length; i++)
            {
                WireProtocol.Commands.Debugging_Thread_Stack.Reply.Call call = calls[i];
                WireProtocol.Commands.Debugging_Thread_Stack.Reply.CallEx callEx = call as WireProtocol.Commands.Debugging_Thread_Stack.Reply.CallEx;

                if (callEx != null)
                {
                    if ((callEx.m_flags & WireProtocol.Commands.Debugging_Thread_Stack.Reply.c_AppDomainTransition) != 0)
                    {
                        //No internal frame is used in the nanoCLR.  This is simply to display the AppDomain transition 
                        //in the callstack of Visual Studio.
                        frames.Add(new CorDebugInternalFrame(this, CorDebugInternalFrameType.STUBFRAME_APPDOMAIN_TRANSITION));
                    }

                    if ((callEx.m_flags & WireProtocol.Commands.Debugging_Thread_Stack.Reply.c_PseudoStackFrameForFilter) != 0)
                    {
                        //No internal frame is used in the nanoCLR for filters.  This is simply to display the transition 
                        //in the callstack of Visual Studio.
                        frames.Add(new CorDebugInternalFrame(this, CorDebugInternalFrameType.STUBFRAME_M2U));
                        frames.Add(new CorDebugInternalFrame(this, CorDebugInternalFrameType.STUBFRAME_U2M));
                    }

                    if ((callEx.m_flags & WireProtocol.Commands.Debugging_Thread_Stack.Reply.c_MethodKind_Interpreted) != 0)
                    {
                        if(lastFrameWasUnmanaged)
                        {
                            frames.Add(new CorDebugInternalFrame(this, CorDebugInternalFrameType.STUBFRAME_U2M));
                        }

                        lastFrameWasUnmanaged = false;
                    }
                    else
                    {
                        if(!lastFrameWasUnmanaged)
                        {
                            frames.Add(new CorDebugInternalFrame(this, CorDebugInternalFrameType.STUBFRAME_M2U));
                        }

                        lastFrameWasUnmanaged = true;
                    }
                }


                frames.Add(new CorDebugFrame (this, call, i));
            }

            m_frames = (CorDebugFrame[])frames.ToArray(typeof(CorDebugFrame));

            uint depthCLR = 0;
            for(int iFrame = m_frames.Length - 1; iFrame >= 0; iFrame--)
            {
                m_frames[iFrame]._depthCLR = depthCLR;
                depthCLR++;
            }
        }

        public CorDebugThread Thread
        {
            [DebuggerHidden]
            get { return m_thread; }
        }

        public Engine Engine
        {
            get { return m_thread.Engine; }
        }

        public uint NumFrames        
        {            
            [DebuggerHidden]
            get {return (uint)m_frames.Length;}
        }

        public void RefreshFrames ()
        {
            // The frames need to be different after resuming execution
            if (m_frames != null)
            {
                for (int iFrame = 0; iFrame < m_frames.Length; iFrame++)
                {
                    m_frames[iFrame] = m_frames[iFrame].Clone ();
                }
            }
        }

        public CorDebugFrame GetFrameFromDepthCLR (uint depthCLR)
        {
            int index = m_frames.Length - 1 - (int)depthCLR;

            if (Utility.InRange(index, 0, m_frames.Length-1))
                return m_frames[index];
            return null;
        }

        public CorDebugFrame GetFrameFromDepthnanoCLR (uint depthnanoCLR)
        {
            for(uint iFrame = depthnanoCLR; iFrame < m_frames.Length; iFrame++)
            {
                CorDebugFrame frame = m_frames[iFrame];

                if(frame.DepthnanoCLR == depthnanoCLR)
                {
                    return frame;
                }
            }

            return null;
        }

        public CorDebugFrame ActiveFrame
        {
            get { return GetFrameFromDepthCLR(0); }
        }

        #region ICorDebugChain Members

        int ICorDebugChain.IsManaged( out int pManaged )
        {
            pManaged = Boolean.TRUE;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetThread( out ICorDebugThread ppThread )
        {
            ppThread = m_thread.GetRealCorDebugThread();

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetReason( out CorDebugChainReason pReason )
        {
            pReason = m_thread.IsVirtualThread ? CorDebugChainReason.CHAIN_FUNC_EVAL : CorDebugChainReason.CHAIN_NONE;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. EnumerateFrames( out ICorDebugFrameEnum ppFrames )
        {
            //Reverse the order for the enumerator
            CorDebugFrame[] frames = new CorDebugFrame[m_frames.Length];

            int iFrameSrc = m_frames.Length - 1;
            for(int iFrameDst = 0; iFrameDst < m_frames.Length; iFrameDst++)
            {
                frames[iFrameDst] = m_frames[iFrameSrc];
                iFrameSrc--;
            }

            ppFrames = new CorDebugEnum( frames, typeof( ICorDebugFrame ), typeof( ICorDebugFrameEnum ) );

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetCallee( out ICorDebugChain ppChain )
        {
            ppChain = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetCaller( out ICorDebugChain ppChain )
        {
            ppChain = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetNext( out ICorDebugChain ppChain )
        {
            //Chains are attached from top of the stack to the bottom.
            //This corresponds to the last virtual thread stack to the first
            CorDebugThread thread = m_thread.PreviousThread;

            ppChain = (thread != null) ? thread.Chain : null;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetPrevious( out ICorDebugChain ppChain )
        {
            CorDebugThread thread = m_thread.NextThread;

            ppChain = (thread != null) ? thread.Chain : null;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetContext( out ICorDebugContext ppContext )
        {
            ppContext = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetRegisterSet( out ICorDebugRegisterSet ppRegisters )
        {
            // CorDebugChain.GetRegisterSet is not implemented
            ppRegisters = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetActiveFrame( out ICorDebugFrame ppFrame )
        {
            ppFrame = ActiveFrame;

            return COM_HResults.S_OK;
        }

        int ICorDebugChain. GetStackRange( out ulong pStart, out ulong pEnd )
        {
            ulong u;

            Debug.Assert( m_frames[0]._depthCLR == (m_frames.Length - 1) );
            Debug.Assert( m_frames[m_frames.Length - 1]._depthCLR == 0 );

            CorDebugFrame.GetStackRange(Thread, 0, out pStart, out u );
            CorDebugFrame.GetStackRange(Thread, (uint)(m_frames.Length - 1), out u, out pEnd );

            return COM_HResults.S_OK;
        }

        #endregion
    }
}
