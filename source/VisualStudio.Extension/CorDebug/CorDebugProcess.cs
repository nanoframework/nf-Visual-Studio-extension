//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using BreakpointDef = nanoFramework.Tools.Debugger.WireProtocol.Commands.Debugging_Execution_BreakpointDef;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugProcess : ICorDebugController, ICorDebugProcess, ICorDebugProcess2, IDebugProcess2, IDebugProcessEx2, IDisposable
    {
        CorDebug _corDebug;
        Queue _events;
        ArrayList _threads;
        ArrayList _assemblies;
        ArrayList _appDomains;
        CorDebugAppDomain _appDomainCurrent;
        string[] _assemblyPaths;
        ArrayList _breakpoints;
        Engine _engine;
        bool _executionPaused;
        AutoResetEvent _eventDispatch;
        ManualResetEvent _eventExecutionPaused;
        ManualResetEvent _eventProcessExited;
        EventWaitHandle[] _eventsStopped;
        uint _pid;
        bool _fUpdateBreakpoints;
        int _cStopped;
        DebugPort _debugPort;
        bool _fLaunched;       //whether the attach was from a launch (vs. an attach)
        bool _terminating;
        Utility.Kernel32.CreateThreadCallback _dummyThreadDelegate;
        AutoResetEvent _dummyThreadEvent;
        int _cEvalThreads;
        Hashtable _tdBuiltin;
        ScratchPadArea _scratchPad;
        ulong _fakeAssemblyAddressNext;
        readonly object _syncTerminatingObject;
        Thread _threadDispatch;

        const ulong c_fakeAddressStart = 0x100000000;

        public Guid GuidProcessId { get; }

        public static string DeployDeviceName => "/CorDebug_DeployDeviceName:";

        public CorDebugProcess(DebugPort debugPort, NanoDeviceBase nanoDevice)
        {
            _debugPort = debugPort;
            Device = nanoDevice;
            GuidProcessId = Guid.NewGuid();
            _syncTerminatingObject = new object();
        }

        public void Dispose() 
        {
          Dispose(true);
          GC.SuppressFinalize(this); 
        }

        bool m_fDisposed = false;

        protected virtual void Dispose(bool disposing) 
        {
            if (!m_fDisposed)
            {
                if (disposing)
                {
                    // free managed stuff
                }

                // free "unmanaged" stuff
                StopDebugging(); 
                m_fDisposed = true;
            }
        }

        ~CorDebugProcess()
        {
           Dispose (false);
        }


        public NanoDeviceBase Device { get; }

        public CorDebug CorDebug => _corDebug;

        public bool IsAttachedToEngine => _engine != null;

        public void SetPid(uint pid)
        {
            if (_pid != 0)
                throw new ArgumentException("PID already set");

            _pid = pid;
        }

        public ScratchPadArea ScratchPad => _scratchPad;

        public bool IsDebugging => _corDebug != null;

        public void SetCurrentAppDomain( CorDebugAppDomain appDomain )
        {
            if(appDomain != _appDomainCurrent)
            {
                if(appDomain != null && Engine.Capabilities.AppDomains)
                {
                    Engine.SetCurrentAppDomain(appDomain.Id);
                }
            }

            _appDomainCurrent = appDomain;
        }

        private void OnProcessExit(object sender, EventArgs args)
        {
            uint errorCode = 0;
            bool isProcess = false;

            try
            {
                if (sender is Process process)
                {
                    // this is a "Process"
                    isProcess = true;

                    errorCode = (uint)process.ExitCode;
                }
            }
            catch
            {
            }

            if(!isProcess)
            {
                // try sender as Engine
                try
                {
                    Engine engine = sender as Engine;
                    engine.Stop();
                    engine.Dispose();
                    engine = null;
                    _engine = null;

                    GC.WaitForPendingFinalizers();
                }
                catch
                {
                }
            }

            ICorDebugProcess.Terminate(errorCode);
        }

        private void Init(CorDebug corDebug, bool fLaunch)
        {
            try
            {
                if (IsDebugging)
                    throw new Exception("CorDebugProcess is already in debugging mode before Init() has run");

                _corDebug = corDebug;
                _fLaunched = fLaunch;

                _events = Queue.Synchronized(new Queue());
                _threads = new ArrayList();
                _assemblies = new ArrayList();
                _appDomains = new ArrayList();
                _breakpoints = ArrayList.Synchronized(new ArrayList());
                _executionPaused = true;
                _eventDispatch = new AutoResetEvent(false);
                _eventExecutionPaused = new ManualResetEvent(true);
                _eventProcessExited = new ManualResetEvent(false);
                _eventsStopped = new EventWaitHandle[] { _eventExecutionPaused, _eventProcessExited };
                _fUpdateBreakpoints = false;
                _cStopped = 0;
                _terminating = false;
                _cEvalThreads = 0;
                _tdBuiltin = null;
                _scratchPad = new ScratchPadArea(this);
                _fakeAssemblyAddressNext = c_fakeAddressStart;
                _threadDispatch = null;

                _corDebug.RegisterProcess(this);
            }
            catch (Exception)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.DeploymentErrorDeviceErrors);
                throw;
            }
        }

        private void UnInit()
        {
            if (_assemblies != null)
            {
                foreach (CorDebugAssembly assembly in _assemblies)
                {
                    ((IDisposable)assembly).Dispose();
                }

                _assemblies = null;
            }

            if (_debugPort != null)
            {
                _debugPort.RemoveProcess(Device);
                _debugPort = null;
            }

            _appDomains = null;
            _events = null;
            _threads = null;
            _assemblyPaths = null;
            _breakpoints = null;
            _executionPaused = false;
            _eventDispatch = null;
            _fUpdateBreakpoints = false;
            _cStopped = 0;
            _fLaunched = false;
            _terminating = false;
            _dummyThreadEvent = null;
            _cEvalThreads = 0;
            _tdBuiltin = null;
            _scratchPad = null;

            _threadDispatch = null;

            if (_corDebug != null)
            {
                _corDebug.UnregisterProcess(this);
                _corDebug = null;
            }
        }

        public void DirtyBreakpoints()
        {
            _fUpdateBreakpoints = true;

            if (!IsExecutionPaused)
            {
                UpdateBreakpoints();
            }
        }

        public void DetachFromEngine()
        {
            lock (this)
            {
                if (_engine != null)
                {
                    _engine.OnMessage -= new MessageEventHandler(OnMessage);
                    _engine.OnCommand -= new CommandEventHandler(OnCommand);
                    _engine.OnNoise -= new NoiseEventHandler(OnNoise);
                    _engine.OnProcessExit -= new EventHandler(OnProcessExit);

                    // better do this inside a try/catch for unexpected side effects from the dispose and finalizer
                    try
                    {
                        _engine.Stop();
                        _engine.Dispose();

                        (Device as NanoDeviceBase).Disconnect();
                    }
                    catch
                    {
                    }

                    _engine = null;

                    GC.WaitForPendingFinalizers();
                }
            }
        }

        private void EnsureProcessIsInInitializedState()
        {
            if (!_engine.IsDeviceInInitializeState())
            {
                bool fSucceeded = false;

                MessageCentre.StartProgressMessage(Resources.ResourceStrings.Rebooting);

                for(int retries = 0; retries < 5; retries++)
                {
                    if(_engine.ConnectionSource == ConnectionSource.nanoCLR)
                    {
                        if (_engine.IsDeviceInInitializeState())
                        {
                            fSucceeded = true;
                            break;
                        }

                        _engine.RebootDevice(RebootOptions.ClrOnly | RebootOptions.WaitForDebugger);

                        // better pause here to allow the reboot to occur
                        // use a back-off strategy of increasing the wait time to accommodate slower or less responsive targets (such as networked ones)
                        Thread.Sleep(500 * (retries + 1));

                        //Thread.Yield();
                    }
                    else if(_engine.ConnectionSource == ConnectionSource.nanoBooter)
                    {
                        // this is telling nanoBooter to enter CLR
                        _engine.ExecuteMemory(0);

                        Thread.Yield();
                    }
                    else
                    {
                        // unknown connection source?!
                        // shouldn't be here, but...
                        // ...maybe this is caused by a comm timeout because the target is rebooting
                        Thread.Yield();
                    }
                }
                
                if (!ShuttingDown && !fSucceeded)
                {
                    MessageCentre.StopProgressMessage();
                    throw new Exception(Resources.ResourceStrings.CouldNotReconnect);
                }
            }

            MessageCentre.StopProgressMessage(Resources.ResourceStrings.TargetInitializeSuccess);
        }

        public Engine AttachToEngine()
        {
            int maxRetries     = 5;
            int retrySleepTime = 500;

            for(int retry = 0; retry < maxRetries; retry++)
            {
                if (ShuttingDown)
                {
                    break;
                }
                
                try
                {
                    lock (this)
                    {
                        if (_engine == null)
                        {
                            if(Device.DebugEngine == null)
                            {
                                Device.CreateDebugEngine();
                            }

                            _engine = Device.DebugEngine;// new Engine(Device.Parent, Device as INanoDevice);
                        }

                        // make sure there is only one handler, so remove whatever is there before adding a new one
                        _engine.OnMessage -= new MessageEventHandler(OnMessage);
                        _engine.OnMessage += new MessageEventHandler(OnMessage);
                        _engine.OnCommand -= new CommandEventHandler(OnCommand);
                        _engine.OnCommand += new CommandEventHandler(OnCommand);
                        _engine.OnNoise -= new NoiseEventHandler(OnNoise);
                        _engine.OnNoise += new NoiseEventHandler(OnNoise);
                        _engine.OnProcessExit -= new EventHandler(OnProcessExit);
                        _engine.OnProcessExit += new EventHandler(OnProcessExit);

                        _engine.ThrowOnCommunicationFailure = false;
                        _engine.StopDebuggerOnConnect = true;
                    }

                    var connect = ThreadHelper.JoinableTaskFactory.Run(
                        async () =>
                        {
                           return await  _engine.ConnectAsync(retrySleepTime, true, ConnectionSource.Unknown);
                        }
                    );

                    if (connect)
                    {
                        _engine.ThrowOnCommunicationFailure = true;
                        _engine.SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State.SourceLevelDebugging, 0);

                        break;
                    }

                    Thread.Yield();
                }
                catch
                {
                    DetachFromEngine();

                    if(!ShuttingDown)
                    {
                        Thread.Yield();
                    }
                }
            }

            if(_engine != null && !_engine.IsConnected)
            {
                DetachFromEngine();

                return null;
            }

            return _engine;
        }

        public void EnqueueEvent(ManagedCallbacks.ManagedCallback cb)
        {
            _events.Enqueue(cb);
            _eventDispatch.Set();
        }

        public void EnqueueEvents(List<ManagedCallbacks.ManagedCallback> callbacks)
        {
            for (int i = 0; i < callbacks.Count; i++)
            {
                _events.Enqueue(callbacks[i]);
            }

            _eventDispatch.Set();
        }

        private bool FlushEvent()
        {
            bool fContinue = false;
            ManagedCallbacks.ManagedCallback mc = null;

            lock(this)
            {
                if(_cStopped == 0 && AnyQueuedEvents)
                {
                    Interlocked.Increment( ref _cStopped );

                    mc = (ManagedCallbacks.ManagedCallback)_events.Dequeue();
                }
            }

            if(mc != null)
            {
                DebugAssert(ShuttingDown || IsExecutionPaused || mc is ManagedCallbacks.ManagedCallbackDebugMessage, "Error on FlushEvent");

                mc.Dispatch( _corDebug.ManagedCallback );
                fContinue = true;
            }

            return fContinue;
        }

        private void FlushEvents()
        {
            while (FlushEvent())
            {
            }
        }

        private bool ShuttingDown
        {
            get
            {
                return _terminating;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void UpdateExecutionIfNecessary()
        {
            DebugAssert(_cStopped >= 0, "Error on start updating execution: " + _cStopped.ToString());

            if (_cStopped > 0)
            {
                PauseExecution();
            }
            else if (!AnyQueuedEvents)
            {
                if (IsExecutionPaused)
                {
                    /*
                        * cStopped count is not maintained on the nanoCLR.
                        * There is a race condition where we try to resume execution
                        * while the nanoCLR is telling us that a breakpoint is hit.  Specifically,
                        * the following can occur.
                        *
                        * 1.  CorDebug tells nanoCLR to resume
                        * 2.  nanoCLR hits a breakpoint
                        * 3.  CorDebug tells nanoCLR to stop
                        * 4.  CorDebug tells nanoCLR to resume
                        * 5.  CorDebug is notified that breakpoint is hit.
                        *
                        * Draining breakpoints is necessary here to avoid the race condition
                        * that step 4 should not be allowed to occur until all breakpoints are drained.
                        * This amounts to breakpoints being skipped.
                        *
                        * Note the asymmetry here.  CorDebug can at any time tell nanoCLR to stop, and be sure that execution
                        * is stopped.  But CorDebug should not tell nanoCLR to resume execution if there are any outstanding
                        * breakpoints available, regardless of whether CorDebug has been notified yet.
                        */
                    DrainBreakpoints();

                    if (!AnyQueuedEvents)
                    {
                        ResumeExecution();
                    }
                }
            }

            DebugAssert(_cStopped >= 0, "Error on completing execution update: " + _cStopped.ToString());
        }

        private void DispatchEvents()
        {
            MessageCentre.InternalErrorMessage(false, Resources.ResourceStrings.DispatchEvents);

            try
            {
                while (IsAttachedToEngine && !ShuttingDown)
                {
                    _eventDispatch.WaitOne();
                    FlushEvents();

                    UpdateExecutionIfNecessary();
                }
            }
            catch (Exception e)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.DispatchEventsFailed);

                DebugAssert(!(IsAttachedToEngine && !ShuttingDown), "Event dispatch failed:" + e.Message);
                ICorDebugProcess.Terminate(0);
            }
        }

        private void EnqueueStartupEventsAndWait()
        {
            DebugAssert(_pid != 0, "Error queueing start-up events. Process is not 0.");    //cpde will fail
            DebugAssert(Device != null, "Error queueing start-up events.Device is null.");

            EnqueueEvent(new ManagedCallbacks.ManagedCallbackProcess(this, ManagedCallbacks.ManagedCallbackProcess.EventType.CreateProcess));

            CorDebugAppDomain appDomain = new CorDebugAppDomain(this, 1);
            _appDomains.Add(appDomain);

            EnqueueEvent(new ManagedCallbacks.ManagedCallbackAppDomain(appDomain, ManagedCallbacks.ManagedCallbackAppDomain.EventType.CreateAppDomain));

            WaitDummyThread();

            //Dispatch the CreateProcess/CreateAppDomain events, so that VS will hopefully shut down nicely from now on
            FlushEvents();

            DebugAssert(_cStopped == 0, "Error queueing start-up events. Stopped is " + _cStopped.ToString());
            DebugAssert(!AnyQueuedEvents, "Error queueing start-up events. There are no queued events.");
        }

        private void StartClr()
        {
            try
            {
                EnqueueStartupEventsAndWait();
                
                MessageCentre.DebugMessage(String.Format(Resources.ResourceStrings.AttachingToDevice));

                if (AttachToEngine() == null)
                {
                    MessageCentre.DebugMessage(String.Format(Resources.ResourceStrings.DeploymentErrorReconnect));
                    throw new Exception(Resources.ResourceStrings.DebugEngineAttachmentFailure);
                }

                CorDebugBreakpointBase breakpoint = new CLREventsBreakpoint(this);

                if (_fLaunched)
                {
                    //This will reboot the device if start debugging was done without a deployment
    
                    MessageCentre.DebugMessage(String.Format(Resources.ResourceStrings.WaitingDeviceInitialization));
                    EnsureProcessIsInInitializedState();

                    // need to force a connection to the device after ensuring that the device is properly initialized
                    // this is needed to make sure that all engine flags are properly set
                    var connect = ThreadHelper.JoinableTaskFactory.Run(
                        async () =>
                        {
                            return await _engine.ConnectAsync(5000, true, ConnectionSource.Unknown);
                        }
                    );

                    // forced update device info in order to get deployed assemblies
                    Device.GetDeviceInfo(true);

                    // need to update the assemblies right here or the collection won't be populated when we need it ahead
                    UpdateAssemblies();
                }
                else
                {
                    IsExecutionPaused = false;
                    PauseExecution();

                    UpdateAssemblies();
                    UpdateThreadList();

                    if (_threads.Count == 0)
                    {
                        //Check to see if the device has exited
                        if (_engine.IsDeviceInExitedState())
                        {
                            MessageCentre.DebugMessage(Resources.ResourceStrings.DebuggingTargetNotFound);
                            throw new ProcessExitException();
                        }
                    }
                }
            }
            catch(Exception)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.InitializationFailed);
                throw;
            }
        }

        private void UpdateThreadList()
        {
        /*
          This is a bit of a hack (or performance improvement, if you prefer)
          The nanoCLR creates threads with wild abandon, but ICorDebug specifies that
          thread creation/destruction events should stop the CLR, and provide callbacks
          This can slow down debugging anything that makes heavy use of threads
          For example...managed drivers, timers, etc...
          So we are faking the events just in time, in a couple of cases --
          when a real breakpoint gets hit, when execution is stopped via BreakAll, etc..
         */

            MessageCentre.InternalErrorMessage(Resources.ResourceStrings.RunningThreadsInformation);

            uint[] threads = _engine.GetThreadList();

            if (threads != null)
            {
                ArrayList threadsDeleted = (ArrayList)_threads.Clone();

                //Find new threads to create
                foreach (uint pid in threads)
                {
                    CorDebugThread thread = GetThread(pid);

                    if (thread == null)
                    {
                        AddThread(new CorDebugThread(this, pid, null));
                    }
                    else
                    {
                        threadsDeleted.Remove(thread);
                    }
                }

                //Find old threads to delete
                foreach (CorDebugThread thread in threadsDeleted)
                {
                    RemoveThread(thread);
                }
            }
        }

        public void StartDebugging(CorDebug corDebug, bool fLaunch)
        {
            try
            {
                if (_pid == 0)
                    throw new Exception(Resources.ResourceStrings.BogusCorDebugProcess);

                Init(corDebug, fLaunch);

                _threadDispatch = new Thread(delegate()
                {
                    try
                    {
                        MessageCentre.StartProgressMessage(Resources.ResourceStrings.DebuggingStarting);

                        StartClr();
                        DispatchEvents();
                    }
                    catch (Exception ex)
                    {
                        MessageCentre.StopProgressMessage();

                        ICorDebugProcess.Terminate(0);
                        MessageCentre.DebugMessage(String.Format(Resources.ResourceStrings.DebuggerThreadTerminated, ex.Message));
                    }
                    finally
                    {
                        DebugAssert(ShuttingDown, "Error starting debug. Engine has shutting down flag set.");

                        _eventProcessExited.Set();

                        if (_terminating)
                        {
                            ManagedCallbacks.ManagedCallbackProcess mc = new ManagedCallbacks.ManagedCallbackProcess(this, ManagedCallbacks.ManagedCallbackProcess.EventType.ExitProcess);

                            mc.Dispatch(_corDebug.ManagedCallback);
                        }

                        StopDebugging();
                    }
                });
                _threadDispatch.Start();
            }
            catch (Exception)
            {
                ICorDebugProcess.Terminate(0);
                throw;
            }
        }

        public Engine Engine
        {
            [DebuggerHidden]
            get { return _engine; }
        }

        public AD_PROCESS_ID PhysicalProcessId
        {
            get
            {
                AD_PROCESS_ID id = new AD_PROCESS_ID
                {
                    ProcessIdType = (uint)AD_PROCESS_ID_TYPE.AD_PROCESS_ID_SYSTEM,
                    dwProcessId = _pid
                };
                return id;
            }
        }

        public bool IsInEval
        {
            get { return _cEvalThreads > 0; }
        }

        public class ScratchPadArea
        {
            private CorDebugProcess m_process;
            private WeakReference[] m_scratchPad;

            public ScratchPadArea(CorDebugProcess process)
            {
                m_process = process;
            }

            private void ReallocScratchPad(int size)
            {
                DebugAssert(m_scratchPad != null && m_scratchPad.Length < size, "Error reallocating scratchpad.");
                m_process.Engine.ResizeScratchPad(size);

                //Refresh scratch pad values
                for (int iScratchPad = 0; iScratchPad < m_scratchPad.Length; iScratchPad++)
                {
                    WeakReference wr = m_scratchPad[iScratchPad];

                    if (wr != null)
                    {
                        CorDebugValue val = (CorDebugValue) wr.Target;

                        if (val != null)
                        {
                            RuntimeValue rtv = m_process.Engine.GetScratchPadValue(iScratchPad);

                            val.RuntimeValue = rtv;
                        }
                    }
                }

                WeakReference[] scratchPadT = m_scratchPad;
                m_scratchPad = new WeakReference[size];
                scratchPadT.CopyTo(m_scratchPad, 0);
            }

            public int ReserveScratchBlockHelper()
            {
                if (m_scratchPad == null)
                    m_scratchPad = new WeakReference[0];

                for (int i = 0; i < m_scratchPad.Length; i++)
                {
                    WeakReference wr = m_scratchPad[i];

                    if (wr == null || wr.Target == null)
                        return i;
                }

                return -1;
            }

            public int ReserveScratchBlock()
            {
                int index = ReserveScratchBlockHelper();

                if (index < 0 && m_scratchPad.Length > 0)
                {
                    GC.Collect();
                    index = ReserveScratchBlockHelper();
                }

                if (index < 0)
                {
                    index = m_scratchPad.Length;
                    ReallocScratchPad(m_scratchPad.Length + 50);
                }

                return index;
            }

            public void Clear()
            {
                if (m_scratchPad != null && m_scratchPad.Length > 0)
                {
                    m_process.Engine.ResizeScratchPad(0);
                    m_scratchPad = null;
                }
            }

            public CorDebugValue GetValue(int index, CorDebugAppDomain appDomain)
            {
                WeakReference wr = m_scratchPad[index];

                if (wr == null)
                {
                    wr = new WeakReference(null);
                    m_scratchPad[index] = wr;
                }

                CorDebugValue val = (CorDebugValue) wr.Target;

                if (val == null)
                {
                    val = CorDebugValue.CreateValue(m_process.Engine.GetScratchPadValue(index), appDomain);
                    wr.Target = val;
                }

                return val;
            }
        }

        public CorDebugThread GetThread(uint id)
        {
            foreach (CorDebugThread thread in _threads)
            {
                if (id == thread.ID)
                    return thread;
            }

            return null;
        }

        public void AddThread(CorDebugThread thread)
        {
            DebugAssert(!_threads.Contains(thread), "Error adding thread");

            _threads.Add(thread);
            if (thread.IsVirtualThread)
            {
                if (_cEvalThreads == 0)
                {
                    Engine.SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State.NoCompaction | Commands.DebuggingExecutionChangeConditions.State.PauseTimers, 0);
                }

                _cEvalThreads++;
            }
            else
            {
                EnqueueEvent(new ManagedCallbacks.ManagedCallbackThread(thread, ManagedCallbacks.ManagedCallbackThread.EventType.CreateThread));
            }
        }

        public void RemoveThread(CorDebugThread thread)
        {
            if (_threads.Contains(thread))
            {
                thread.Exited = true;

                _threads.Remove(thread);
                if (thread.IsVirtualThread)
                {
                    CorDebugEval eval = thread.CurrentEval;
                    eval.EndEval(CorDebugEval.EvalResult.Complete, false);

                    _cEvalThreads--;
                    if (_cEvalThreads == 0)
                    {
                        Engine.SetExecutionMode(0, Commands.DebuggingExecutionChangeConditions.State.NoCompaction | Commands.DebuggingExecutionChangeConditions.State.PauseTimers);
                    }
                }
                else
                {
                    EnqueueEvent(new ManagedCallbacks.ManagedCallbackThread(thread, ManagedCallbacks.ManagedCallbackThread.EventType.ExitThread));
                }
            }
        }

        public bool IsExecutionPaused
        {
            get { return _executionPaused; }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                _executionPaused = value;

                if (_executionPaused)
                    _eventExecutionPaused.Set();
                else
                    _eventExecutionPaused.Reset();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void PauseExecution()
        {
            if (!IsExecutionPaused)
            {
                if (_engine.PauseExecution())
                {
#if NO_THREAD_CREATED_EVENTS
                UpdateThreadList();
#endif
                    IsExecutionPaused = true;
                }
                else
                {
                    DebugAssert(_terminating, "Error pausing execution. Terminating flag is set.");
                }

                _eventExecutionPaused.Set();
            }
        }

        //[MethodImpl(MethodImplOptions.Synchronized)]
        private void ResumeExecution(bool fForce)
        {
            if ((fForce || IsExecutionPaused) && IsDebugging)
            {
                foreach (CorDebugThread thread in _threads)
                {
                    thread.ResumingExecution();
                }

                if (_cEvalThreads == 0)
                {
                    ScratchPad.Clear();
                }

                UpdateBreakpoints();
                Engine.ResumeExecution();

                SetCurrentAppDomain(null);

                IsExecutionPaused = false;
            }
        }

        private void ResumeExecution()
        {
            ResumeExecution(false);
        }

        public CorDebugAssembly AssemblyFromIdx(uint idx)
        {
            return CorDebugAssembly.AssemblyFromIdx( idx, _assemblies );
        }

        public CorDebugAssembly AssemblyFromIndex(uint index)
        {
            return CorDebugAssembly.AssemblyFromIndex( index, _assemblies );
        }

        public void RegisterBreakpoint(object o, bool fRegister)
        {
            if (fRegister)
            {
                _breakpoints.Add(o);
            }
            else
            {
                _breakpoints.Remove(o);
            }

            DirtyBreakpoints();
        }

        internal ArrayList GetBreakpoints( Type t, CorDebugAppDomain appDomain )
        {
            ArrayList al = new ArrayList( _breakpoints.Count );

            foreach(CorDebugBreakpointBase breakpoint in _breakpoints)
            {
                if(t.IsAssignableFrom( breakpoint.GetType() ))
                {
                    if(appDomain == null || appDomain == breakpoint.AppDomain)
                    {
                        al.Add( breakpoint );
                    }
                }
            }

            return al;
        }

        private CorDebugBreakpointBase[] FindBreakpoints( BreakpointDef breakpointDef )
        {
            //perhaps need to cheat for CorDebugEval.Breakpoint for uncaught exceptions...
            ArrayList breakpoints = new ArrayList( 1 );
            if( IsDebugging )
            {
                foreach( CorDebugBreakpointBase breakpoint in _breakpoints )
                {
                    if( breakpoint.IsMatch( breakpointDef ) )
                    {
                        breakpoints.Add( breakpoint );
                    }
                }
            }

            return (CorDebugBreakpointBase[])breakpoints.ToArray( typeof( CorDebugBreakpointBase ) );
        }

        private bool BreakpointHit(BreakpointDef breakpointDef)
        {
#if NO_THREAD_CREATED_EVENTS
            UpdateThreadList();
#endif
            CorDebugBreakpointBase[] breakpoints = FindBreakpoints(breakpointDef);
            bool fStopExecution = false;

            for(int iBreakpoint = 0; iBreakpoint < breakpoints.Length; iBreakpoint++)
            {
                CorDebugBreakpointBase breakpoint = breakpoints[iBreakpoint];

                if(breakpoint.ShouldBreak(breakpointDef))
                {
                    fStopExecution = true;
                    break;
                }
            }

            if(fStopExecution)
            {
                for(int iBreakpoint = 0; iBreakpoint < breakpoints.Length; iBreakpoint++)
                {
                    CorDebugBreakpointBase breakpoint = breakpoints[iBreakpoint];
                    breakpoint.Hit( breakpointDef );
                }
            }

            return fStopExecution;
        }

        public void UpdateBreakpoints()
        {
            if(!IsAttachedToEngine || !_fUpdateBreakpoints || ShuttingDown)
                return;

            //Function breakpoints are set for each AppDomain.
            //No need to send all duplicates to the nanoCLR
            ArrayList al = new ArrayList( _breakpoints.Count );
            for(int i = 0; i < _breakpoints.Count; i++)
            {
                CorDebugBreakpointBase breakpoint1 = ((CorDebugBreakpointBase)_breakpoints[i]);

                bool fDuplicate = false;

                for(int j = 0; j < i; j++)
                {
                    CorDebugBreakpointBase breakpoint2 = ((CorDebugBreakpointBase)_breakpoints[j]);

                    if(breakpoint1.Equals( breakpoint2 ))
                    {
                        fDuplicate = true;
                        break;
                    }
                }

                if(!fDuplicate)
                {
                    al.Add( breakpoint1.Debugging_Execution_BreakpointDef );
                }
            }

            BreakpointDef[] breakpointDefs = (BreakpointDef[])al.ToArray( typeof( BreakpointDef ) );

            Engine.SetBreakpoints(breakpointDefs);
            _fUpdateBreakpoints = false;
        }

        private void StoreAssemblyPaths(string[] args)
        {
            DebugAssert(_assemblyPaths == null, "Error storing assembly paths. Paths are null.");

            ArrayList al = new ArrayList(args.Length);

            for (int iArg = 0; iArg < args.Length; iArg++)
            {
                string arg = args[iArg];
                if (arg == "-load")
                {
                    al.Add(args[++iArg]);
                }
                else if (arg.StartsWith("/load:"))
                {
                    al.Add(arg.Substring("/load:".Length));
                }
            }

            AssemblyPaths = (string[])al.ToArray(typeof(string));
        }

        public string[] AssemblyPaths
        {
            get { return _assemblyPaths; }
            set
            {
                if (_assemblyPaths != null)
                    throw new ArgumentException("already stored assembly paths");

                _assemblyPaths = value;
            }
        }

        /*
            DummyThreadStart is an incredibly big hack.  It is exclusively a workaround for a cdpe shortcoming.  cpde requires
            a valid Win32 thread handle returned from CreateProcess.  DummyThread is created for that reason.  Also note
            that cpde is not expecting to receive events until ResumeThread is called.  So DummyThread gets suspended,
            and when it gets resumed by cpde, it then can start sending events.
        */
        private void DummyThreadStart(IntPtr ptr)
        {
            _dummyThreadEvent.Set();
        }

        private void CreateDummyThread(out IntPtr threadHandle, out uint threadId)
        {
            _dummyThreadDelegate = new Utility.Kernel32.CreateThreadCallback(DummyThreadStart);
            _dummyThreadEvent = new AutoResetEvent(false);

            threadHandle = Utility.Kernel32.CreateThread(IntPtr.Zero, 0, _dummyThreadDelegate, IntPtr.Zero, Utility.Kernel32.CREATE_SUSPENDED, out threadId);
        }

        private void WaitDummyThread()
        {
            if (_dummyThreadEvent == null)
                throw new Exception("The device debuggee proxy thread is not initialized");
            _dummyThreadEvent.WaitOne();
            _dummyThreadEvent = null;
            _dummyThreadDelegate = null;
        }

        //private void CreateEmulatorProcess(
        //    DebugPort   port,
        //    string      lpApplicationName,
        //    string      lpCommandLine,
        //    IntPtr      lpProcessAttributes,
        //    IntPtr      lpThreadAttributes,
        //    int         bInheritHandles,
        //    uint        dwCreationFlags,
        //    System.IntPtr lpEnvironment,
        //    string      lpCurrentDirectory,
        //    ref _STARTUPINFO lpStartupInfo,
        //    ref _PROCESS_INFORMATION lpProcessInformation,
        //    uint        debuggingFlags
        //    )
        //{
        //    MessageCentre.DebugMessage(String.Format(Resources.ResourceStrings.EmulatorCommandLine, lpCommandLine));

        //    try
        //    {
        //        Process emuProcess = new Process();
        //        emuProcess.StartInfo.FileName = lpApplicationName;
        //        emuProcess.StartInfo.Arguments = lpCommandLine.Substring(lpApplicationName.Length+2);
        //        emuProcess.StartInfo.UseShellExecute = false;

        //        emuProcess.StartInfo.RedirectStandardOutput = true;
        //        emuProcess.StartInfo.RedirectStandardError = true;
        //        emuProcess.StartInfo.RedirectStandardInput = false;

        //        // Set our event handler to asynchronously read the emulator's outputs.
        //        emuProcess.OutputDataReceived += new DataReceivedEventHandler(MessageCentre.OutputMsgHandler);
        //        emuProcess.ErrorDataReceived += new DataReceivedEventHandler(MessageCentre.ErrorMsgHandler);

        //        emuProcess.StartInfo.WorkingDirectory = lpCurrentDirectory;

        //        // Start the process.
        //        if(!emuProcess.Start())
        //            throw new Exception("Process.Start() returned false.");

        //        // Start the asynchronous reads of the emulator's output streams
        //        emuProcess.BeginOutputReadLine();
        //        emuProcess.BeginErrorReadLine();

        //        this.PortDefinition = new PortDefinition_Emulator("Emulator", emuProcess.Id);
        //        this.SetPid((uint)emuProcess.Id);

        //        port.AddProcess(this);

        //        const int DUPLICATE_SAME_ACCESS = 0x00000002;
        //        Utility.Kernel32.DuplicateHandle(Utility.Kernel32.GetCurrentProcess(), emuProcess.Handle,
        //                                         Utility.Kernel32.GetCurrentProcess(), out lpProcessInformation.hProcess,
        //                                         0, false, DUPLICATE_SAME_ACCESS);
                
        //        lpProcessInformation.dwProcessId = (uint)emuProcess.Id;
        //        CreateDummyThread(out lpProcessInformation.hThread, out lpProcessInformation.dwThreadId);
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new ApplicationException(String.Format("Could not create emulator process."),ex);
        //    }
        //}

        private void CreateDeviceProcess(DebugPort port, string lpApplicationName, string lpCommandLine, System.IntPtr lpProcessAttributes, System.IntPtr lpThreadAttributes, int bInheritHandles, uint dwCreationFlags, System.IntPtr lpEnvironment, string lpCurrentDirectory, ref _STARTUPINFO lpStartupInfo, ref _PROCESS_INFORMATION lpProcessInformation, uint debuggingFlags)
        {
            bool fDidDeploy = true;     //What if we did a launch but no deploy to a device...

            if (!fDidDeploy)
            {
                Engine.RebootDevice(RebootOptions.ClrOnly | RebootOptions.WaitForDebugger);
            }

            DebugAssert(_debugPort == port, "Error creating device process.");

            lpProcessInformation.dwProcessId = _pid;

            CreateDummyThread(out lpProcessInformation.hThread, out lpProcessInformation.dwThreadId);
        }

        private void InternalCreateProcess(
            DebugPort   port,
            string      lpApplicationName,
            string      lpCommandLine,
            IntPtr      lpProcessAttributes,
            IntPtr      lpThreadAttributes,
            int         bInheritHandles,
            uint        dwCreationFlags,
            System.IntPtr lpEnvironment,
            string      lpCurrentDirectory,
            ref _STARTUPINFO lpStartupInfo,
            ref _PROCESS_INFORMATION lpProcessInformation,
            uint        debuggingFlags
            )
        {
            //if (port.IsLocalPort)
            //    this.CreateEmulatorProcess(port, lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags, lpEnvironment, lpCurrentDirectory, ref lpStartupInfo, ref lpProcessInformation, debuggingFlags);
            //else
            CreateDeviceProcess(port, lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags, lpEnvironment, lpCurrentDirectory, ref lpStartupInfo, ref lpProcessInformation, debuggingFlags);
        }

        public static CorDebugProcess CreateProcessEx(IDebugPort2 pPort, string lpApplicationName, string lpCommandLine, System.IntPtr lpProcessAttributes, System.IntPtr lpThreadAttributes, int bInheritHandles, uint dwCreationFlags, System.IntPtr lpEnvironment, string lpCurrentDirectory, ref _STARTUPINFO lpStartupInfo, ref _PROCESS_INFORMATION lpProcessInformation, uint debuggingFlags)
        {
            if (!(pPort is DebugPort port))
            {
                throw new Exception("IDebugPort2 object passed to nanoFramework package by Visual Studio is not a valid device port");
            }

            CommandLineBuilder cb = new CommandLineBuilder(lpCommandLine);
            string[] args = cb.Arguments;
            string deployDeviceName = args[args.Length-1];

            //Extract deployDeviceName
            if (!deployDeviceName.StartsWith(CorDebugProcess.DeployDeviceName))
                throw new Exception(String.Format("\"{0}\" does not appear to be a valid nanoFramework device name", CorDebugProcess.DeployDeviceName));

            deployDeviceName = deployDeviceName.Substring(CorDebugProcess.DeployDeviceName.Length);
            cb.RemoveArguments(args.Length - 1, 1);

            lpCommandLine = cb.ToString();

            CorDebugProcess process = port.GetDeviceProcess(deployDeviceName, 60);

            if (process == null)
                throw new Exception("CorDebugProcess.CreateProcessEx() could not create or find the device process");

            process.StoreAssemblyPaths(args);
            process.InternalCreateProcess(port, lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags, lpEnvironment, lpCurrentDirectory, ref lpStartupInfo, ref lpProcessInformation, debuggingFlags);

            return process;
        }

        internal ulong FakeLoadAssemblyIntoMemory( CorDebugAssembly assembly )
        {
            ulong address = _fakeAssemblyAddressNext;

            assembly.ICorDebugModule.GetSize(out uint size);

            _fakeAssemblyAddressNext += size;

            return address;
        }

        private void LoadAssemblies()
        {
            MessageCentre.DebugMessage(Resources.ResourceStrings.LoadAssemblies);

            List<Commands.DebuggingResolveAssembly> assemblies = Engine.ResolveAllAssemblies();
            string[] assemblyPathsT = new string[1];
            Pdbx.PdbxFile.Resolver resolver = new Pdbx.PdbxFile.Resolver();

            DebugAssert(assemblies.Count > 0, "Error loading assemblies. Assemblies count is not 0.");

            if(assemblies.Count == 0)
            {
                // if debug was started, presumably after a successful deployment, there have to be assemblies on the device
                // so, if there are none, probably the command above failed, anyway we can't proceed with debugging

                throw new Exception("Device is reporting no assemblies deployed. Can't debug!");
            }

            if (!_fLaunched)
            {
                //Find mscorlib
                Commands.DebuggingResolveAssembly a = null;
                Commands.DebuggingResolveAssembly.Reply reply = null;

                for (int i = 0; i < assemblies.Count; i++)
                {
                    a = assemblies[i];
                    reply = a.Result;

                    if (reply.Name == "mscorlib")
                        break;
                }

                DebugAssert(reply.Name == "mscorlib", "Error loading assemblies. Couldn't find mscorlib.");

                //PlatformInfo platformInfo = new PlatformInfo(asyVersion); // by not specifying any runtime information, we will look for the most suitable version
                // TODO need to point to debug output folder
                //resolver.AssemblyDirectories = platformInfo.AssemblyFolders;
            }

            for (int i = 0; i < assemblies.Count; i++)
            {
                Commands.DebuggingResolveAssembly a = assemblies[i];

                CorDebugAssembly assembly = AssemblyFromIdx(a.Idx);

                if (assembly == null)
                {
                    Commands.DebuggingResolveAssembly.Reply reply = a.Result;

                    if (!string.IsNullOrEmpty(reply.Path))
                    {
                        assemblyPathsT[0] = reply.Path;

                        resolver.AssemblyPaths = assemblyPathsT;
                    }
                    else
                    {
                        resolver.AssemblyPaths = _assemblyPaths;
                    }

                    Pdbx.PdbxFile pdbxFile = resolver.Resolve(reply.Name, reply.Version, Engine.IsTargetBigEndian); //Pdbx.PdbxFile.Open(reply.Name, reply.m_version, assemblyPaths);

                    assembly = new CorDebugAssembly(this, reply.Name, pdbxFile, nanoCLR_TypeSystem.IdxAssemblyFromIndex(a.Idx));

                    _assemblies.Add(assembly);
                }
            }
        }

        public CorDebugAppDomain GetAppDomainFromId( uint id )
        {
            foreach(CorDebugAppDomain appDomain in _appDomains)
            {
                if(appDomain.Id == id)
                {
                    return appDomain;
                }
            }

            return null;
        }

        private void CreateAppDomainFromId(uint id)
        {
            CorDebugAppDomain appDomain = new CorDebugAppDomain(this, id);

            _appDomains.Add(appDomain);
            EnqueueEvent(new ManagedCallbacks.ManagedCallbackAppDomain(appDomain, ManagedCallbacks.ManagedCallbackAppDomain.EventType.CreateAppDomain));
        }

        private void RemoveAppDomain(CorDebugAppDomain appDomain)
        {
            EnqueueEvent(new ManagedCallbacks.ManagedCallbackAppDomain(appDomain, ManagedCallbacks.ManagedCallbackAppDomain.EventType.ExitAppDomain));
            _appDomains.Remove(appDomain);
        }

        public void UpdateAssemblies()
        {
            MessageCentre.InternalErrorMessage(Resources.ResourceStrings.LoadedAssembliesInformation);
            lock (_appDomains)
            {
                LoadAssemblies();

                uint[] appDomains = new uint[] { CorDebugAppDomain.c_AppDomainId_ForNoAppDomainSupport };

                if (Engine.Capabilities.AppDomains)
                {
                    appDomains = Engine.GetAppDomains().Data;
                }

                //Search for new appDomains
                foreach (uint id in appDomains)
                {
                    CorDebugAppDomain appDomain = GetAppDomainFromId(id);

                    if (appDomain == null)
                    {
                        CreateAppDomainFromId(id);
                    }
                }

                for (int iAppDomain = 0; iAppDomain < _appDomains.Count; iAppDomain++)
                {
                    CorDebugAppDomain appDomain = (CorDebugAppDomain)_appDomains[iAppDomain];
                    appDomain.UpdateAssemblies();
                }

                //Search for dead appDomains
                for (int iAppDomain = _appDomains.Count - 1; iAppDomain >= 0; iAppDomain--)
                {
                    CorDebugAppDomain appDomain = (CorDebugAppDomain)_appDomains[iAppDomain];

                    if (Array.IndexOf(appDomains, appDomain.Id) < 0)
                    {
                        RemoveAppDomain(appDomain);
                    }
                }
            }
        }

        private void StopDebugging()
        {
            DebugAssert(ShuttingDown, "Error stopping debug. Shutdown flag is set.");

            /*
             * this is called when debugging stops, either via terminate or detach.
             */

            try
            {
                if (_engine != null) //perhaps we never attached.
                {
                    _engine.ThrowOnCommunicationFailure = false;

                    // need to reboot device to clear memory leaks which are caused by the running app stopping execution and leaving C/C++ vars orphaned in the CRT heap
                    _engine.RebootDevice(RebootOptions.NormalReboot);

                    DetachFromEngine();
                }
            }
            catch (Exception ex)
            {
                MessageCentre.InternalErrorMessage(false, "Exception while terminating CorDebugProcess: " + ex.Message);
            }
            finally
            {
                UnInit();
            }
        }

        public void OnMessage(IncomingMessage msg, string text)
        {
            if (_threads != null && _threads.Count > 0)
            {
                if(_appDomains != null && _appDomains.Count > 0)
                {
                  EnqueueEvent( new ManagedCallbacks.ManagedCallbackDebugMessage( (CorDebugThread)_threads[0], (CorDebugAppDomain)_appDomains[0], "nanoCLR_Message", text, LoggingLevelEnum.LStatusLevel0 ) );
                }
            }
            else
            {
                MessageCentre.DebugMessage(text);
            }

        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void DrainBreakpoints()
        {
            bool fStopExecution = false;
            bool fAnyBreakpoints = false;
            bool fLastBreakpoint = true;
            BreakpointDef bp;

            if (Engine == null) return;

            do
            {
                bp = Engine.GetBreakpointStatus();

                if (bp == null || bp.m_flags == 0)
                {
                    break;
                }

                fLastBreakpoint = (bp.m_flags & BreakpointDef.c_LAST_BREAKPOINT) != 0;

                //clear c_LAST_BREAKPOINT flags because so derived breakpoint classes don't need to care
                bp.m_flags = (ushort)(bp.m_flags & ~BreakpointDef.c_LAST_BREAKPOINT);

                bool fStopExecutionT = BreakpointHit(bp);

                fStopExecution = fStopExecution || fStopExecutionT;
                fAnyBreakpoints = true;
            } while (!fLastBreakpoint);

            if (fAnyBreakpoints)
            {
                if (!fStopExecution)
                {
                    DebugAssert(!IsExecutionPaused, "Error draining breakpoints. Execution is not stopped.");
                    //Force execution to resume, even though IsExecutionPaused state is not set
                    //hitting a breakpoint requires the nanoCLR to be stopped.  Setting IsExecutionPaused = true
                    //just to force resumeExecution to succeed is wrong, and will result in a race condition
                    //where Stop is waiting for a synchronous stop, will succeed while execution is about to be resumed.
                    ResumeExecution(true);
                }
                else
                {
                    IsExecutionPaused = true;
                }
            }
        }

        public void SuspendCommands(bool fSuspend)
        {
            if (fSuspend)
            {
                bool lockTaken = false;
                Monitor.Enter(this, ref lockTaken);
                if (lockTaken)
                {
                    Interlocked.Increment(ref _cStopped); //also don't dispatch events
                }
            }
            else
            {
                Interlocked.Decrement(ref _cStopped);
                _eventDispatch.Set();                 //just in case there events needing dispatch.
                Monitor.Exit(this);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void OnCommand(IncomingMessage msg, bool fReply)
        {
            switch (msg.Header.Cmd)
            {
                case Commands.c_Debugging_Execution_BreakpointHit:
                    DrainBreakpoints();
                    break;
                case Commands.c_Monitor_ProgramExit:
                    ICorDebugProcess.Terminate(0);
                    break;
                case Commands.c_Monitor_Ping:
                case Commands.c_Debugging_Button_Report:
                case Commands.c_Debugging_Value_GetStack:
                    //nop
                    break;
                default:
                    MessageCentre.InternalErrorMessage(false, "Unexpected command=" + msg.Header.Cmd);
                    break;
            }
        }

        public void OnNoise(byte[] buf, int offset, int count)
        {
            if(buf != null && (offset + count) <= buf.Length)
            {
                MessageCentre.InternalErrorMessage( System.Text.UTF8Encoding.UTF8.GetString(buf, offset, count) );
            }
        }

        private ArrayList GetAllNonVirtualThreads()
        {
            ArrayList al = new ArrayList(_threads.Count);

            foreach (CorDebugThread thread in _threads)
            {
                if (!thread.IsVirtualThread)
                    al.Add(thread);
            }

            return al;
        }

        public class BuiltinType
        {
            private CorDebugAssembly m_assembly;
            private readonly uint m_tkCLR;
            private readonly CorDebugClass m_class;

            public BuiltinType( CorDebugAssembly assembly, uint tkCLR, CorDebugClass cls )
            {
                m_assembly = assembly;
                m_tkCLR = tkCLR;
                m_class = cls;
            }

            public CorDebugAssembly GetAssembly( CorDebugAppDomain appDomain )
            {
                return appDomain.AssemblyFromIdx( m_assembly.Idx );
            }

            public CorDebugClass GetClass( CorDebugAppDomain appDomain )
            {
                CorDebugAssembly assembly = GetAssembly( appDomain );

                return assembly.GetClassFromTokenCLR(m_tkCLR);
            }

            public uint TokenCLR
            {
                get { return m_tkCLR; }
            }
        }

        private void AddBuiltInType(object o, CorDebugAssembly assm, string type)
        {
            uint tkCLR = MetaData.Helper.ClassTokenFromName(assm.MetaDataImport, type);
            CorDebugClass c = assm.GetClassFromTokenCLR(tkCLR);

            BuiltinType builtInType = new BuiltinType( assm, tkCLR, c );

            _tdBuiltin[o] = builtInType;
        }

        public BuiltinType ResolveBuiltInType(object o)
        {
            DebugAssert(o is CorElementType || o is ReflectionDefinition.Kind || o is Debugger.RuntimeDataType, "Error resolving built-in type.");

            CorDebugAssembly assmCorLib = null;

            if (_tdBuiltin == null)
            {
                _tdBuiltin = new Hashtable();

                foreach (CorDebugAssembly assm in _assemblies)
                {
                    if (assm.Name == "mscorlib")
                    {
                        assmCorLib = assm;
                        break;
                    }
                }

                DebugAssert(assmCorLib != null, "Error resolving built-in type. Couldn't find mscorlib");

                AddBuiltInType(CorElementType.ELEMENT_TYPE_BOOLEAN, assmCorLib, "System.Boolean");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_CHAR,    assmCorLib, "System.Char");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_I1, assmCorLib, "System.SByte");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_U1, assmCorLib, "System.Byte");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_I2, assmCorLib, "System.Int16");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_U2, assmCorLib, "System.UInt16");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_I4, assmCorLib, "System.Int32");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_U4, assmCorLib, "System.UInt32");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_I8, assmCorLib, "System.Int64");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_U8, assmCorLib, "System.UInt64");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_R4, assmCorLib, "System.Single");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_R8, assmCorLib, "System.Double");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_CLASS, assmCorLib, "System.Object"); //???
                AddBuiltInType(CorElementType.ELEMENT_TYPE_OBJECT, assmCorLib, "System.Object");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_SZARRAY, assmCorLib, "System.Array");
                AddBuiltInType(CorElementType.ELEMENT_TYPE_ARRAY, assmCorLib, "System.Array");

                AddBuiltInType(ReflectionDefinition.Kind.REFLECTION_TYPE, assmCorLib, "System.RuntimeType");
                AddBuiltInType(ReflectionDefinition.Kind.REFLECTION_TYPE_DELAYED, assmCorLib, "System.RuntimeType");
                AddBuiltInType(ReflectionDefinition.Kind.REFLECTION_ASSEMBLY, assmCorLib, "System.Reflection.Assembly");
                AddBuiltInType(ReflectionDefinition.Kind.REFLECTION_FIELD, assmCorLib, "System.Reflection.RuntimeFieldInfo");
                AddBuiltInType(ReflectionDefinition.Kind.REFLECTION_METHOD, assmCorLib, "System.Reflection.RuntimeMethodInfo");
                AddBuiltInType(ReflectionDefinition.Kind.REFLECTION_CONSTRUCTOR, assmCorLib, "System.Reflection.RuntimeConstructorInfo");

                AddBuiltInType(RuntimeDataType.DATATYPE_TRANSPARENT_PROXY, assmCorLib, "System.Runtime.Remoting.Proxies.__TransparentProxy" );
            }

            return (BuiltinType)_tdBuiltin[o];
        }

        private bool AnyQueuedEvents => _events.Count > 0;

        private ICorDebugProcess ICorDebugProcess => (ICorDebugProcess)this;

        private ICorDebugController ICorDebugController => (ICorDebugController)this;

        #region ICorDebugController Members

        int ICorDebugController.Stop(uint dwTimeout)
        {
            Interlocked.Increment(ref _cStopped);
            _eventDispatch.Set();

            DebugAssert(IsExecutionPaused || Thread.CurrentThread != _threadDispatch, "Error stopping controller.");
            EventWaitHandle.WaitAny(_eventsStopped);

            return COM_HResults.S_OK;
        }

        int ICorDebugController.Continue(int fIsOutOfBand)
        {
            Interlocked.Decrement(ref _cStopped);
            _eventDispatch.Set();

            return COM_HResults.S_OK;
        }

        int ICorDebugController.IsRunning(out int pbRunning)
        {
            pbRunning = Boolean.BoolToInt(!IsExecutionPaused);

            return COM_HResults.S_OK;
        }

        int ICorDebugController.HasQueuedCallbacks(ICorDebugThread pThread, out int pbQueued)
        {
            CorDebugThread thread = pThread as CorDebugThread;
            bool fQueued = false;

            if (thread == null || !thread.SuspendThreadEvents)
            {
                lock (_events.SyncRoot)
                {
                    foreach (ManagedCallbacks.ManagedCallback mc in _events)
                    {
                        ManagedCallbacks.ManagedCallbackThread mct = mc as ManagedCallbacks.ManagedCallbackThread;

                        if (thread == null)
                        {
                            //any non-thread events? should
                            //any thread events that aren't visible to cpde
                            fQueued = (mct == null || !mct.Thread.SuspendThreadEvents);
                        }
                        else
                        {
                            //only visible, thread events, matching the requested thread.
                            fQueued = (mct != null && mct.Thread == thread && !mct.Thread.SuspendThreadEvents);
                        }

                        if (fQueued)
                        {
                            break;
                        }
                    }
                }
            }

            pbQueued = Boolean.BoolToInt(fQueued);

            return COM_HResults.S_OK;
        }

        int ICorDebugController.EnumerateThreads(out ICorDebugThreadEnum ppThreads)
        {
            ppThreads = new CorDebugEnum(GetAllNonVirtualThreads(), typeof(ICorDebugThread), typeof(ICorDebugThreadEnum));

            return COM_HResults.S_OK;
        }

        int ICorDebugController.SetAllThreadsDebugState(CorDebugThreadState state, ICorDebugThread pExceptThisThread)
        {
            // could/should really make this one call to engine...or else send them all at once..
            CorDebugThread threadExcept = (CorDebugThread)pExceptThisThread;

            foreach (CorDebugThread thread in GetAllNonVirtualThreads())
            {
                if (thread != threadExcept)
                {
                    DebugAssert(!thread.GetLastCorDebugThread().IsVirtualThread, "Error setting all threads to debug state.");
                    ((ICorDebugThread)thread).SetDebugState(state);
                }
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugController.Detach()
        {
            lock (_syncTerminatingObject)
            {
                if (!ShuttingDown)
                {
                    _eventDispatch.Set();
                }
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugController.Terminate(uint exitCode)
        {
            lock (_syncTerminatingObject)
            {
                if (!ShuttingDown)
                {
                    _terminating = true;

                    if (IsDebugging)
                    {
                        _eventDispatch.Set();
                    }
                    else
                    {
                        StopDebugging();
                    }
                }
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugController.CanCommitChanges(uint cSnapshots, ref ICorDebugEditAndContinueSnapshot pSnapshots, out ICorDebugErrorInfoEnum pError)
        {
            // CorDebugProcess.CanCommitChanges is not implemented
            pError = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugController.CommitChanges(uint cSnapshots, ref ICorDebugEditAndContinueSnapshot pSnapshots, out ICorDebugErrorInfoEnum pError)
        {
            // CorDebugProcess.CommitChanges is not implemented
            pError = null;

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugProcess Members

        int ICorDebugProcess.Stop(uint dwTimeout)
        {
            return ICorDebugController.Stop(dwTimeout);
        }

        int ICorDebugProcess.Continue(int fIsOutOfBand)
        {
            return ICorDebugController.Continue(fIsOutOfBand);
        }

        int ICorDebugProcess.IsRunning(out int pbRunning)
        {
            return ICorDebugController.IsRunning(out pbRunning);
        }

        int ICorDebugProcess.HasQueuedCallbacks(ICorDebugThread pThread, out int pbQueued)
        {
            return ICorDebugController.HasQueuedCallbacks(pThread, out pbQueued);
        }

        int ICorDebugProcess.EnumerateThreads(out ICorDebugThreadEnum ppThreads)
        {
            return ICorDebugController.EnumerateThreads(out ppThreads);
        }

        int ICorDebugProcess.SetAllThreadsDebugState(CorDebugThreadState state, ICorDebugThread pExceptThisThread)
        {
            return ICorDebugController.SetAllThreadsDebugState(state, pExceptThisThread);
        }

        int ICorDebugProcess.Detach()
        {
            return ICorDebugController.Detach();
        }

        int ICorDebugProcess.Terminate(uint exitCode)
        {
            return ICorDebugController.Terminate(exitCode);
        }

        int ICorDebugProcess.CanCommitChanges(uint cSnapshots, ref ICorDebugEditAndContinueSnapshot pSnapshots, out ICorDebugErrorInfoEnum pError)
        {
            return ICorDebugController.CanCommitChanges(cSnapshots, ref pSnapshots, out pError);
        }

        int ICorDebugProcess.CommitChanges(uint cSnapshots, ref ICorDebugEditAndContinueSnapshot pSnapshots, out ICorDebugErrorInfoEnum pError)
        {
            return ICorDebugController.CommitChanges(cSnapshots, ref pSnapshots, out pError);
        }

        int ICorDebugProcess.GetID(out uint pdwProcessId)
        {
            pdwProcessId = _pid;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.GetHandle(out uint phProcessHandle)
        {
            // CorDebugProcess.GetHandle is not implemented
            phProcessHandle = 0;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.GetThread(uint dwThreadId, out ICorDebugThread ppThread)
        {
            ppThread = GetThread(dwThreadId);

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.EnumerateObjects(out ICorDebugObjectEnum ppObjects)
        {
            // CorDebugProcess.EnumerateObjects is not implemented
            ppObjects = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.IsTransitionStub(ulong address, out int pbTransitionStub)
        {
            pbTransitionStub = Boolean.FALSE;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.IsOSSuspended(uint threadID, out int pbSuspended)
        {
            // CorDebugProcess.IsOSSuspended is not implemented
            pbSuspended = Boolean.FALSE;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.GetThreadContext(uint threadID, uint contextSize, IntPtr context)
        {
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess.SetThreadContext(uint threadID, uint contextSize, IntPtr context)
        {
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess.ReadMemory(ulong address, uint size, byte[] buffer, out uint read)
        {
            read = 0;

            if (address + size == 0x100000000)
            {
            }
            else if (address >= c_fakeAddressStart)
            {
                //find the assembly loaded at this address (all fake, of course)
                for (int iAssembly = 0; iAssembly < _assemblies.Count; iAssembly++)
                {
                    CorDebugAssembly assembly = (CorDebugAssembly)_assemblies[iAssembly];

                    assembly.ICorDebugModule.GetBaseAddress(out ulong baseAddress);
                    assembly.ICorDebugModule.GetSize(out uint assemblySize);

                    if (address >= baseAddress && address < baseAddress + assemblySize)
                    {
                        DebugAssert(address + size <= baseAddress + assemblySize, "Error reading memory.");

                        assembly.ReadMemory(address - baseAddress, size, buffer, out read);
                    }
                }
            }
            else
            {
                var readMemory = Engine.ReadMemory((uint)address, size);

                if (readMemory.Success)
                {
                    DebugAssert(readMemory.Buffer.Length == size, "Error reading memory. Buffer length is different then size.");

                    readMemory.Buffer.CopyTo(buffer, 0);
                    read = size;
                }
            }

            return COM_HResults.BOOL_TO_HRESULT_FAIL(read > 0);
        }

        int ICorDebugProcess.WriteMemory(ulong address, uint size, byte[] buffer, out uint written)
        {
            written = 0;

            if (address < c_fakeAddressStart)
            {
                var (ErrorCode, Success) = Engine.WriteMemory((uint)address, buffer);

                if (Success)
                {
                    written = size;
                }
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.ClearCurrentException(uint threadID)
        {
            CorDebugThread thread = GetThread(threadID);
            if (thread != null)
                ((ICorDebugThread)thread).ClearCurrentException();

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.EnableLogMessages(int fOnOff)
        {
            // CorDebugProcess.EnableLogMessages is not implemented
            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.ModifyLogSwitch(string pLogSwitchName, int lLevel)
        {
            // Need to adjust Interop assembly CorDebugInterop, to enable this function
            // CorDebugProcess.ModifyLogSwitch is not implemented
            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.EnumerateAppDomains(out ICorDebugAppDomainEnum ppAppDomains)
        {
            ppAppDomains = new CorDebugEnum(_appDomains, typeof(ICorDebugAppDomain), typeof(ICorDebugAppDomainEnum));

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.GetObject(out ICorDebugValue ppObject)
        {
            // CorDebugProcess.GetObject is not implemented
            ppObject = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.ThreadForFiberCookie(uint fiberCookie, out ICorDebugThread ppThread)
        {
            // CorDebugProcess.ThreadForFiberCookie is not implemented
            ppThread = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess.GetHelperThreadID(out uint pThreadID)
        {
            // CorDebugProcess.GetHelperThreadID is not implemented
            pThreadID = 0;

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugProcess2 Members

        int ICorDebugProcess2.GetVersion( out _COR_VERSION version )
        {
            version = new _COR_VERSION
            {
                dwMajor = 1    //This is needed to handle v1 exceptions.
            };

            return COM_HResults.S_OK;
        }

        int ICorDebugProcess2.GetThreadForTaskID( ulong taskid, out ICorDebugThread2 ppThread )
        {
            ppThread = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess2.SetUnmanagedBreakpoint( ulong address, uint bufsize, byte[] buffer, out uint bufLen )
        {
            bufLen = 0;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess2.GetDesiredNGENCompilerFlags( out uint pdwFlags )
        {
            pdwFlags = 0;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess2.SetDesiredNGENCompilerFlags( uint pdwFlags )
        {
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess2.ClearUnmanagedBreakpoint( ulong address )
        {
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugProcess2.GetReferenceValueFromGCHandle(UIntPtr handle, out ICorDebugReferenceValue pOutValue)
        {
            pOutValue = null;

            return COM_HResults.E_NOTIMPL;
        }

        #endregion

        #region IDebugProcess2 Members

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.Detach()
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.Attach(IDebugEventCallback2 pCallback, Guid[] rgguidSpecificEngines, uint celtSpecificEngines, int[] rghrEngineAttach)
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.CauseBreak()
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetAttachedSessionName(out string pbstrSessionName)
        {
            pbstrSessionName = null;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetProcessId(out Guid pguidProcessId)
        {
            pguidProcessId = GuidProcessId;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.EnumThreads(out IEnumDebugThreads2 ppEnum)
        {
            // DebugProcess.EnumThreads is not implemented
            //If we need to implement this, must return a copy without any VirtualThreads
            ppEnum = null;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.Terminate()
        {
            return ICorDebugProcess.Terminate(0);
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetServer(out IDebugCoreServer2 ppServer)
        {
            ppServer = _debugPort.DebugPortSupplier.CoreServer;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.EnumPrograms(out IEnumDebugPrograms2 ppEnum)
        {
            ArrayList appDomains = _appDomains;

            if(!IsAttachedToEngine)
            {
                //need to fake this in order to get the Attach Dialog to work.
                DebugAssert( appDomains == null, "Error enumerating programs. AppDomain is null.");
                appDomains = new ArrayList
                {
                    new CorDebugAppDomain(this, 1)
                };
            }

            ppEnum = new CorDebugEnum( appDomains, typeof( IDebugProgram2 ), typeof( IEnumDebugPrograms2 ) );
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetPhysicalProcessId(AD_PROCESS_ID[] pProcessId)
        {
            pProcessId[0] = PhysicalProcessId;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetInfo(enum_PROCESS_INFO_FIELDS Fields, PROCESS_INFO[] pProcessInfo)
        {
            PROCESS_INFO pi = new PROCESS_INFO
            {
                Fields = Fields
            };

            if (_debugPort == null)
            {
                return COM_HResults.E_FAIL;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_ATTACHED_SESSION_NAME) != 0)
            {
                pi.bstrAttachedSessionName = null;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_BASE_NAME) != 0)
            {
                pi.bstrBaseName = Device.Description;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_CREATION_TIME) != 0)
            {
                pi.CreationTime.dwHighDateTime = 0;
                pi.CreationTime.dwLowDateTime = 0;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_FILE_NAME) != 0)
            {
                pi.bstrFileName = Device.Description;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_FLAGS) != 0)
            {
                if(_executionPaused)
                    pi.Flags = enum_PROCESS_INFO_FLAGS.PIFLAG_PROCESS_STOPPED;
                else
                    pi.Flags = enum_PROCESS_INFO_FLAGS.PIFLAG_PROCESS_RUNNING;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_PROCESS_ID) != 0)
            {
                pi.ProcessId = PhysicalProcessId;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_SESSION_ID) != 0)
            {
                //Can/should we enable debugging across sessions?  Can we access other user's
                //debug pipe?
                pi.dwSessionId = 0;
            }

            if ((Fields & enum_PROCESS_INFO_FIELDS.PIF_TITLE) != 0)
            {
                pi.bstrTitle = "<Unknown>";
            }

            pProcessInfo[0] = pi;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.CanDetach()
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetPort(out IDebugPort2 ppPort)
        {
            ppPort = _debugPort;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcess2.GetName(enum_GETNAME_TYPE gnType, out string pbstrName)
        {
            // DebugProcess.GetName is not implemented
            pbstrName = "nanoFramework application";
            return COM_HResults.S_OK;
        }

        #endregion

        #region IDebugProcessEx2 Members

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcessEx2.Detach(IDebugSession2 pSession)
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcessEx2.Attach(IDebugSession2 pSession)
        {
            int hr = COM_HResults.S_OK;

            //check if the process is still alive.  For emulator, if the process is still alive.
            if (!_debugPort.ContainsProcess(this))
            {
                hr = COM_HResults.E_PROCESS_DESTROYED;
            }

            return hr;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugProcessEx2.AddImplicitProgramNodes(ref Guid guidLaunchingEngine, Guid[] rgguidSpecificEngines, uint celtSpecificEngines)
        {
            return COM_HResults.S_OK;
        }

        #endregion


        private static void DebugAssert(bool condition, string message, string detailedMessage)
        {
            MessageCentre.InternalErrorMessage(condition, String.Format("message: {0}\r\nDetailed Message: {1}", message, detailedMessage));
        }

        private static void DebugAssert(bool condition, string message)
        {
            MessageCentre.InternalErrorMessage(condition, message);
        }
    }
}
