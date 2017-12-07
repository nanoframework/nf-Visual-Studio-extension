using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System.Collections;
using System.Diagnostics;
using System;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Debugger
{
    public class CorDebugThread : ICorDebugThread, ICorDebugThread2 /* -- this is needed for v2 exception handling -- InterceptCurrentException */
    {
        CorDebugProcess _process;
        CorDebugChain _chain;
        bool _fSuspendedSav; //for function eval, need to remember if this thread is suspended before suspending for function eval..      
        CorDebugValue _currentException;
        CorDebugEval _eval;
        CorDebugAppDomain _initialAppDomain;

        public CorDebugThread(CorDebugProcess process, uint id, CorDebugEval eval)
        {
            _process = process;
            ID = id;
            _eval = eval;
        }

        public CorDebugEval CurrentEval { get;  }

        public bool Exited { get; set; }

        public bool SuspendThreadEvents { get; set; }

        public void AttachVirtualThread(CorDebugThread thread)
        {
            CorDebugThread threadLast = this.GetLastCorDebugThread();

            threadLast.NextThread = thread;
            thread.PreviousThread = threadLast;

            _process.AddThread(thread);
            Debug.Assert(Process.IsExecutionPaused);

            threadLast._fSuspendedSav = threadLast.IsSuspended;
            threadLast.Suspend().Wait();
        }

        private async Task<bool> Suspend()
        {
            if (!IsSuspended)
            {
                return await this.Engine.SuspendThreadAsync(ID);
            }
            else
            {
                return true;
            }
        }

        private async Task<bool> Resume()
        {
            if (IsSuspended)
            {
                return await this.Engine.ResumeThreadAsync(ID);

            }
            else
            {
                return true;
            }
        }

        public bool RemoveVirtualThread(CorDebugThread thread)
        {
            //can only remove last thread
            CorDebugThread threadLast = this.GetLastCorDebugThread();

            Debug.Assert(threadLast.IsVirtualThread && !this.IsVirtualThread);
            if (threadLast != thread)
                return false;

            CorDebugThread threadNextToLast = threadLast.PreviousThread;

            threadNextToLast.NextThread = null;

            if(_fSuspendedSav)
            {
                Suspend().Wait();
            }
            else
            {
                Resume().Wait();
            }

            threadLast.PreviousThread = null;

            //Thread will be removed from process.m_alThreads when the ThreadTerminated breakpoint is hit
            return true;
        }

        public Engine Engine
        {
            [System.Diagnostics.DebuggerHidden]
            get { return _process.Engine; }
        }

        public CorDebugProcess Process
        {
            [System.Diagnostics.DebuggerHidden]
            get { return _process; }
        }

        public CorDebugAppDomain AppDomain
        {
            get
            {
                CorDebugAppDomain appDomain = _initialAppDomain;

                if(!Exited)
                {

                    CorDebugThread thread = GetLastCorDebugThread();

                    CorDebugFrame frame = thread.Chain.ActiveFrame;

                    appDomain = frame.AppDomain;
                }

                return appDomain;
            }
        }

        public uint ID
        {
            [System.Diagnostics.DebuggerHidden]
            get;
        }

        public void StoppedOnException()
        {
            var getThreadException = Engine.GetThreadExceptionAsync(ID);
            getThreadException.Wait();

            _currentException = CorDebugValue.CreateValue(getThreadException.Result, this.AppDomain);
        }

        //This is the only thread that cpde knows about
        public CorDebugThread GetRealCorDebugThread()
        {
            CorDebugThread thread;

            for (thread = this; thread.PreviousThread != null; thread = thread.PreviousThread) ;

            return thread;
        }

        public CorDebugThread GetLastCorDebugThread()
        {
            CorDebugThread thread;

            for (thread = this; thread.NextThread != null; thread = thread.NextThread) ;

            return thread;
        }

        //Doubly-linked list of virtual threads.  The head of the list is the real thread
        //All other threads (at this point), should be the cause of a function eval

        public CorDebugThread PreviousThread { get; set; }

        public CorDebugThread NextThread { get; set; }

        public bool IsVirtualThread { get { return _eval != null; } }

        public bool IsLogicalThreadSuspended { get { return GetLastCorDebugThread().IsSuspended; } }

        public bool IsSuspended { get; } = false;
        
        public CorDebugChain Chain
        {
            get
            {
                if (_chain == null)
                {
                    var setThreadStack = this.Engine.GetThreadStackAsync(ID);
                    setThreadStack.Wait();

                    Debugger.WireProtocol.Commands.Debugging_Thread_Stack.Reply ts = setThreadStack.Result;

                    if (ts != null)
                    {
                        if((ts.m_flags & Debugger.WireProtocol.Commands.Debugging_Thread_Stack.Reply.TH_F_Suspended) == 0)
                        {
                            Suspend().Wait();
                        }
                        else
                        {
                            Resume().Wait();
                        }

                        _chain = new CorDebugChain(this, ts.m_data);

                        if(_initialAppDomain == null)
                        {
                            CorDebugFrame initialFrame = _chain.GetFrameFromDepthnanoCLR( 0 );
                            _initialAppDomain = initialFrame.AppDomain;
                        }
                    }
                }
                return _chain;
            }
        }

        public void RefreshChain()
        {
            if (_chain != null)
            {
                _chain.RefreshFrames();
            }
        }

        public void ResumingExecution()
        {
            if (IsSuspended)
            {
                RefreshChain();
            }
            else
            {
                _chain = null;
                _currentException = null;
            }
        }

        #region ICorDebugThread Members

        int ICorDebugThread.GetObject(out ICorDebugValue ppObject)
        {
            Debug.Assert(!IsVirtualThread);

            var getThread = Engine.GetThreadAsync(ID);
            getThread.Wait();

            RuntimeValue rv = getThread.Result;

            if (rv != null)
            {
                ppObject = CorDebugValue.CreateValue(rv, this.AppDomain);
            }
            else
            {
                ppObject = null;
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetDebugState(out CorDebugThreadState pState)
        {
            Debug.Assert(!IsVirtualThread);

            pState = IsLogicalThreadSuspended ? CorDebugThreadState.THREAD_SUSPEND : CorDebugThreadState.THREAD_RUN;

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.CreateEval(out ICorDebugEval ppEval)
        {
            Debug.Assert(!IsVirtualThread);

            ppEval = new CorDebugEval(this);

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetHandle(out uint phThreadHandle)
        {
            Debug.Assert(!IsVirtualThread);

            //CorDebugThread.GetHandle is not implemented
            phThreadHandle = 0;

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.SetDebugState(CorDebugThreadState state)
        {
            Debug.Assert(!IsVirtualThread);

            //This isnt' quite right, there is a discrepancy between CorDebugThreadState and CorDebugUserState
            //where the nanoCLR only has one thread state to differentiate between running, waiting, and stopped            
            if (state != CorDebugThreadState.THREAD_RUN)
            {
                Suspend().Wait();
            }
            else
            {
                Resume().Wait();
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetProcess(out ICorDebugProcess ppProcess)
        {
            Debug.Assert(!IsVirtualThread);
            ppProcess = _process;

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.EnumerateChains(out ICorDebugChainEnum ppChains)
        {
            Debug.Assert(!IsVirtualThread);

            ArrayList chains = new ArrayList();

            for (CorDebugThread thread = this.GetLastCorDebugThread(); thread != null; thread = thread.PreviousThread)
            {
                CorDebugChain chain = thread.Chain;

                if (chain != null)
                {
                    chains.Add(chain);
                }
            }

            ppChains = new CorDebugEnum(chains, typeof(ICorDebugChain), typeof(ICorDebugChainEnum));

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetUserState(out CorDebugUserState pState)
        {
            Debug.Assert(!IsVirtualThread);

            // CorDebugThread.GetUserState is not implemented           
            pState = 0;

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetRegisterSet(out ICorDebugRegisterSet ppRegisters)
        {
            Debug.Assert(!IsVirtualThread);

            // CorDebugThread.GetRegisterSet is not implemented
            ppRegisters = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugThread.GetActiveFrame(out ICorDebugFrame ppFrame)
        {
            Debug.Assert(!IsVirtualThread);
            ((ICorDebugChain)this.GetLastCorDebugThread().Chain).GetActiveFrame(out ppFrame);

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetActiveChain(out ICorDebugChain ppChain)
        {
            Debug.Assert(!IsVirtualThread);
            ppChain = this.GetLastCorDebugThread().Chain;

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.ClearCurrentException()
        {
            Debug.Assert(!IsVirtualThread);
            Debug.Assert(false, "API for unmanaged code only?");

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetID(out uint pdwThreadId)
        {
            Debug.Assert(!IsVirtualThread);
            pdwThreadId = ID;

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.CreateStepper(out ICorDebugStepper ppStepper)
        {
            Debug.Assert(!IsVirtualThread);
            ICorDebugFrame frame;

            ((ICorDebugThread)this).GetActiveFrame(out frame);
            ppStepper = new CorDebugStepper((CorDebugFrame)frame);

            return COM_HResults.S_OK;
        }

        int ICorDebugThread.GetCurrentException(out ICorDebugValue ppExceptionObject)
        {
            ppExceptionObject = this.GetLastCorDebugThread()._currentException;

            return COM_HResults.BOOL_TO_HRESULT_FALSE( ppExceptionObject != null );
        }

        int ICorDebugThread.GetAppDomain(out ICorDebugAppDomain ppAppDomain)
        {
            ppAppDomain = ((CorDebugThread)this).AppDomain;

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugThread2 Members

        int ICorDebugThread2.GetActiveFunctions(uint cFunctions, out uint pcFunctions, COR_ACTIVE_FUNCTION[] pFunctions)
        {
            pcFunctions = 0;

            return COM_HResults.E_NOTIMPL;            
        }

        int ICorDebugThread2.GetConnectionID( out uint pdwConnectionId )
        {
            pdwConnectionId = 0;

            return COM_HResults.E_NOTIMPL;            
        }

        int ICorDebugThread2.GetTaskID( out ulong pTaskId )
        {
            pTaskId = 0;

            return COM_HResults.E_NOTIMPL;            
        }

        int ICorDebugThread2.GetVolatileOSThreadID( out uint pdwTid )
        {
            pdwTid = ID;

            return COM_HResults.S_OK;            
        }

        int ICorDebugThread2.InterceptCurrentException( ICorDebugFrame pFrame )
        {
            CorDebugFrame frame = (CorDebugFrame)pFrame;

            this.Engine.UnwindThreadAsync(this.ID, frame.DepthnanoCLR).Wait();

            return COM_HResults.S_OK;
        }

        #endregion

    }
}
