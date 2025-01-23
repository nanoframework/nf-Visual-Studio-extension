//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using System;
using System.Diagnostics;
using BreakpointDef = nanoFramework.Tools.Debugger.WireProtocol.Commands.Debugging_Execution_BreakpointDef;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugStepper : CorDebugBreakpointBase, ICorDebugStepper, ICorDebugStepper2
    {
        //if a stepper steps into/out of a frame, need to update m_frame
        CorDebugFrame m_frame;
        CorDebugThread m_thread;
        COR_DEBUG_STEP_RANGE[] m_ranges;
        CorDebugStepReason m_reasonStopped;
        CorDebugIntercept m_interceptMask;

        public CorDebugStepper( CorDebugFrame frame )
            : base( frame.AppDomain )
        {
            Initialize( frame );
        }

        private void Initialize( CorDebugFrame frame )
        {
            m_frame = frame;
            m_thread = frame.Thread;

            InitializeBreakpointDef();
        }

        private new ushort Kind
        {
            [DebuggerHidden]
            get { return base.Kind; }
            set
            {
                if((value & BreakpointDef.c_STEP_IN) != 0)
                    value |= BreakpointDef.c_STEP_OVER;

                value |= BreakpointDef.c_STEP_OUT | BreakpointDef.c_EXCEPTION_CAUGHT | BreakpointDef.c_THREAD_TERMINATED;

                base.Kind = value;
            }
        }

        private void InitializeBreakpointDef()
        {
            m_breakpointDef.m_depth = m_frame.DepthnanoCLR;
            m_breakpointDef.m_pid = m_thread.ID;

            if(m_ranges != null && m_ranges.Length > 0)
            {
                m_breakpointDef.m_IPStart = m_ranges[0].startOffset;
                m_breakpointDef.m_IPEnd = m_ranges[0].endOffset;
            }
            else
            {
                m_breakpointDef.m_IPStart = 0;
                m_breakpointDef.m_IPEnd = 0;
            }

            Dirty();
        }

        private void Activate( ushort kind )
        {            
            InitializeBreakpointDef();
            Debug.Assert( !Active);
            //currently, we don't support ignoring filters in a step.  cpde always seems to set this flag though.
            //So it may not be very important to support ignoring filters.
            Debug.Assert((m_interceptMask & CorDebugIntercept.INTERCEPT_EXCEPTION_FILTER) != 0);
            Kind = kind;
            Active = true;
        }

        public override bool ShouldBreak( BreakpointDef breakpointDef )
        {
            bool fStop = true;
            CorDebugStepReason reason;

            //optimize, optimize, optimize No reason to get list of threads, and get thread stack for each step!!!            
            ushort flags = breakpointDef.m_flags;
            int depthOld = (int)m_frame.DepthnanoCLR;
            int depthNew = (int)breakpointDef.m_depth;
            int dDepth = depthNew - depthOld;

            if((flags & BreakpointDef.c_STEP) != 0)
            {
                if ((flags & BreakpointDef.c_STEP_IN) != 0)
                {
                    if (Process.Engine.Capabilities.ExceptionFilters && breakpointDef.m_depthExceptionHandler == BreakpointDef.c_DEPTH_STEP_INTERCEPT)
                    {
                        reason = CorDebugStepReason.STEP_INTERCEPT;
                    }
                    else
                    {
                        reason = CorDebugStepReason.STEP_CALL;
                    }
                }
                else if ((flags & BreakpointDef.c_STEP_OVER) != 0)
                {
                    reason = CorDebugStepReason.STEP_NORMAL;
                }
                else
                {
                    if (Process.Engine.Capabilities.ExceptionFilters & breakpointDef.m_depthExceptionHandler == BreakpointDef.c_DEPTH_STEP_EXCEPTION_HANDLER)
                    {
                        reason = CorDebugStepReason.STEP_EXCEPTION_HANDLER;
                    }
                    else
                    {
                        reason = CorDebugStepReason.STEP_RETURN;
                    }
                }
            }
            else if((flags & BreakpointDef.c_EXCEPTION_CAUGHT) != 0)
            {
                reason = CorDebugStepReason.STEP_EXCEPTION_HANDLER;
                if(dDepth > 0)
                    fStop = false;
                else if(dDepth == 0)
                    fStop = (Debugging_Execution_BreakpointDef.m_flags & BreakpointDef.c_STEP_OVER) != 0;
                else
                    fStop = true;
            }
            else if ((flags & BreakpointDef.c_THREAD_TERMINATED) != 0)
            {
                reason = CorDebugStepReason.STEP_EXIT;

                Active = false;
                fStop = false;
            }
            else
            {
                Debug.Assert(false);
                throw new ApplicationException("Invalid stepper hit received");
            }

            if(m_ranges != null && reason == CorDebugStepReason.STEP_NORMAL && breakpointDef.m_depth == Debugging_Execution_BreakpointDef.m_depth)
            {
                foreach(COR_DEBUG_STEP_RANGE range in m_ranges)
                {
                    if(Utility.InRange( breakpointDef.m_IP, range.startOffset, range.endOffset - 1 ))
                    {
                        fStop = false;
                        break;
                    }
                }

                Debug.Assert( Utility.FImplies( m_ranges != null && m_ranges.Length == 1, fStop ) );
            }

            if(fStop && reason != CorDebugStepReason.STEP_EXIT)
            {
                uint depth = breakpointDef.m_depth;
                CorDebugFrame frame = m_thread.Chain.GetFrameFromDepthnanoCLR( depth );

                m_ranges = null;
                Initialize( frame );

                //Will callback with wrong reason if stepping through internal calls?????                
                //If we don't stop at an internal call, we need to reset/remember the range somehow?
                //This might be broken if a StepRange is called that causes us to enter an internal function                
                fStop = !m_frame.Function.IsInternal;
            }

            m_reasonStopped = reason;
            return fStop;
        }

        public override void Hit( BreakpointDef breakpointDef )
        {
            m_ranges = null;
            Active = false;
            Process.EnqueueEvent( new ManagedCallbacks.ManagedCallbackStepComplete( m_frame.Thread, this, m_reasonStopped ) );
        }

        #region ICorDebugStepper Members

        int ICorDebugStepper.IsActive( out int pbActive )
        {
            pbActive = Boolean.BoolToInt(Active);

            return COM_HResults.S_OK;
        }

        int ICorDebugStepper.Deactivate()
        {
            Active = false;

            return COM_HResults.S_OK;
        }

        int ICorDebugStepper.StepRange( int bStepIn, COR_DEBUG_STEP_RANGE[] ranges, uint cRangeCount )
        {
            //This isn't a correct method signature.  However, since we don't support this (yet), it doesn't really matter
            //Add CorDebugStepper.StepRange is not implemented
            m_ranges = ranges;

            Debug.Assert( cRangeCount == 1 );

            for(int iRange = 0; iRange < m_ranges.Length; iRange++)
            {
                COR_DEBUG_STEP_RANGE range = m_ranges[iRange];
                m_ranges[iRange].startOffset = m_frame.Function.GetILnanoCLRFromILCLR( range.startOffset );
                m_ranges[iRange].endOffset = m_frame.Function.GetILnanoCLRFromILCLR( range.endOffset );
            }

            Activate( Boolean.IntToBool( bStepIn ) ? BreakpointDef.c_STEP_IN : BreakpointDef.c_STEP_OVER );

            return COM_HResults.S_OK;
        }

        int ICorDebugStepper.SetUnmappedStopMask( CorDebugUnmappedStop mask )
        {
            return COM_HResults.S_OK;            
        }

        int ICorDebugStepper.SetInterceptMask( CorDebugIntercept mask )
        {
            m_interceptMask = mask;

            return COM_HResults.S_OK;            
        }

        int ICorDebugStepper.Step( int bStepIn )
        {
            m_ranges = null;
            Activate( Boolean.IntToBool( bStepIn ) ? BreakpointDef.c_STEP_IN : BreakpointDef.c_STEP_OVER );

            return COM_HResults.S_OK;
        }

        int ICorDebugStepper.SetRangeIL( int bIL )
        {
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugStepper.StepOut()
        {
            m_ranges = null;
            Activate( BreakpointDef.c_STEP_OUT );

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugStepper2 Members

        int ICorDebugStepper2.SetJMC( int fIsJMCStepper )
        {
            // CorDebugStepper.SetJMC is not implemented
            bool fJMC = Boolean.IntToBool( fIsJMCStepper );
            bool fJMCOld = (Debugging_Execution_BreakpointDef.m_flags & BreakpointDef.c_STEP_JMC) != 0;

            if(fJMC != fJMCOld)
            {
                if(fJMC)
                    Debugging_Execution_BreakpointDef.m_flags |= BreakpointDef.c_STEP_JMC;
                else
                    unchecked { Debugging_Execution_BreakpointDef.m_flags &= (ushort)(~BreakpointDef.c_STEP_JMC); }

                Dirty();
            }

            return COM_HResults.S_OK;
        }

        #endregion
    }
}
