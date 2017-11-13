using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    // AD7Engine is the primary entry point object for the sample engine.
    //
    // It implements:
    //
    // IDebugEngine2: This interface represents a debug engine (DE). It is used to manage various aspects of a debugging session,
    // from creating breakpoints to setting and clearing exceptions.
    //
    // IDebugEngineLaunch2: Used by a debug engine (DE) to launch and terminate programs.
    //
    // IDebugProgram3: This interface represents a program that is running in a process. Since this engine only debugs one process at a time and each
    // process only contains one program, it is implemented on the engine.
    //
    // IDebugEngineProgram2: This interface provides simultaneous debugging of multiple threads in a debuggee.

    [ComVisible(true)]
    [Guid(DebuggerGuids.EngineCLSIDAsString)]
    sealed public class AD7Engine : IDebugEngine2, IDebugEngineLaunch2, IDebugEngine3, IDebugProgram3, IDebugEngineProgram2, IDebugMemoryBytes2, IDebugEngine110
    {
        public const string DebugEngineName = "nanoFramework Debug Engine";

        // used to send events to the debugger. Some examples of these events are thread create, exception thrown, module load.
        private EngineCallback _engineCallback;

        // The sample debug engine is split into two parts: a managed front-end and a mixed-mode back end. DebuggedProcess is the primary
        // object in the back-end. AD7Engine holds a reference to it.
        internal DebuggedProcess DebuggedProcess { get; private set; }

        // This object facilitates calling from this thread into the worker thread of the engine. This is necessary because the Win32 debugging
        // api requires thread affinity to several operations.
        private WorkerThread _pollThread;

        // This object manages breakpoints in the sample engine.
        private BreakpointManager _breakpointManager;

        private Guid _engineGuid = DebuggerGuids.EngineId;

        // A unique identifier for the program being debugged.
        private Guid _ad7ProgramId;

        public AD7Engine()
        {
            _breakpointManager = new BreakpointManager(this);
            //Worker.Initialize();
        }

        ~AD7Engine()
        {
            if (_pollThread != null)
            {
                _pollThread.Close();
            }
        }

        private void Dispose()
        {
            WorkerThread pollThread = _pollThread;
            DebuggedProcess debuggedProcess = DebuggedProcess;

            _engineCallback = null;
            DebuggedProcess = null;
            _pollThread = null;
            _ad7ProgramId = Guid.Empty;

            //debuggedProcess?.Close();
            pollThread?.Close();
        }

        public string GetAddressDescription(uint ip)
        {
            //DebuggedModule module = DebuggedProcess.ResolveAddress(ip);

            return EngineUtils.GetAddressDescription(DebuggedProcess, ip);
        }

        internal EngineCallback Callback
        {
            get { return _engineCallback; }
        }

        #region IDebugEngine2 Members

        public int EnumPrograms(out IEnumDebugPrograms2 ppEnum)
        {
            throw new NotImplementedException();
        }

        public int Attach(IDebugProgram2[] rgpPrograms, IDebugProgramNode2[] rgpProgramNodes, uint celtPrograms, IDebugEventCallback2 pCallback, enum_ATTACH_REASON dwReason)
        {
            throw new NotImplementedException();
        }

        // Creates a pending breakpoint in the engine. A pending breakpoint is contains all the information needed to bind a breakpoint to
        // a location in the debuggee.
        public int CreatePendingBreakpoint(IDebugBreakpointRequest2 pBPRequest, out IDebugPendingBreakpoint2 ppPendingBP)
        {
            Debug.Assert(_breakpointManager != null);
            ppPendingBP = null;

            try
            {
                _breakpointManager.CreatePendingBreakpoint(pBPRequest, out ppPendingBP);
            }
            catch (Exception e)
            {
                return EngineUtils.UnexpectedException(e);
            }

            return Constants.S_OK;
        }

        // Specifies how the DE should handle a given exception.
        // The sample engine does not support exceptions in the debuggee so this method is not actually implemented.
        public int SetException(EXCEPTION_INFO[] pException)
        {
            // TODO
            //DebuggedProcess?.ExceptionManager.SetException(ref pException[0]);
            return Constants.S_OK;
        }

        // Removes the specified exception so it is no longer handled by the debug engine.
        // The sample engine does not support exceptions in the debuggee so this method is not actually implemented.
        public int RemoveSetException(EXCEPTION_INFO[] pException)
        {
            // TODO
            //DebuggedProcess?.ExceptionManager.RemoveSetException(ref pException[0]);
            return Constants.S_OK;
        }

        // Removes the list of exceptions the IDE has set for a particular run-time architecture or language.
        public int RemoveAllSetExceptions(ref Guid guidType)
        {
            // TODO
            //DebuggedProcess?.ExceptionManager.RemoveAllSetExceptions(guidType);
            return Constants.S_OK;
        }

        // Gets the GUID of the DE.
        public int GetEngineId(out Guid pguidEngine)
        {
            pguidEngine = DebuggerGuids.EngineId;
            return Constants.S_OK;
        }

        // Informs a DE that the program specified has been atypically terminated and that the DE should
        // clean up all references to the program and send a program destroy event.
        public int DestroyProgram(IDebugProgram2 pProgram)
        {
            // Tell the SDM that the engine knows that the program is exiting, and that the
            // engine will send a program destroy. We do this because the Win32 debug api will always
            // tell us that the process exited, and otherwise we have a race condition.

            return (AD7_HRESULT.E_PROGRAM_DESTROY_PENDING);
        }

        // Called by the SDM to indicate that a synchronous debug event, previously sent by the DE to the SDM,
        // was received and processed. The only event the sample engine sends in this fashion is Program Destroy.
        // It responds to that event by shutting down the engine.
        public int ContinueFromSynchronousEvent(IDebugEvent2 pEvent)
        {
            //Debug.Assert(Worker.MainThreadId == Worker.CurrentThreadId);

            //try
            //{
            //    if (pEvent is AD7ProgramDestroyEvent)
            //    {
            //        WorkerThread pollThread = m_pollThread;
            //        DebuggedProcess debuggedProcess = m_debuggedProcess;

            //        m_engineCallback = null;
            //        m_debuggedProcess = null;
            //        m_pollThread = null;
            //        m_ad7ProgramId = Guid.Empty;

            //        debuggedProcess.Close();
            //        pollThread.Close();
            //    }
            //    else
            //    {
            //        Debug.Fail("Unknown syncronious event");
            //    }
            //}
            //catch (Exception e)
            //{
            //    return EngineUtils.UnexpectedException(e);
            //}

            return Constants.S_OK;
        }

        // Sets the locale of the DE.
        // This method is called by the session debug manager (SDM) to propagate the locale settings of the IDE so that
        // strings returned by the DE are properly localized. The sample engine is not localized so this is not implemented.
        public int SetLocale(ushort wLangID)
        {
            return Constants.S_OK;
        }

        // Sets the registry root currently in use by the DE. Different installations of Visual Studio can change where their registry information is stored
        // This allows the debugger to tell the engine where that location is.
        public int SetRegistryRoot(string registryRoot)
        {
            // TODO
            //_configStore = new HostConfigurationStore(registryRoot);
            //Logger = Logger.EnsureInitialized(_configStore);
            return Constants.S_OK;
        }

        // A metric is a registry value used to change a debug engine's behavior or to advertise supported functionality.
        // This method can forward the call to the appropriate form of the Debugging SDK Helpers function, SetMetric.
        public int SetMetric(string pszMetric, object varValue)
        {
            if (string.CompareOrdinal(pszMetric, "JustMyCodeStepping") == 0)
            {
                string strJustMyCode = varValue.ToString();
                bool optJustMyCode;
                if (string.CompareOrdinal(strJustMyCode, "0") == 0)
                {
                    optJustMyCode = false;
                }
                else if (string.CompareOrdinal(strJustMyCode, "1") == 0)
                {
                    optJustMyCode = true;
                }
                else
                {
                    return Constants.E_FAIL;
                }

                // TODO
                //_pollThread.RunOperation(new Operation(() => { DebuggedProcess.MICommandFactory.SetJustMyCode(optJustMyCode); }));
                return Constants.S_OK;
            }
            else if (string.CompareOrdinal(pszMetric, "EnableStepFiltering") == 0)
            {
                string enableStepFiltering = varValue.ToString();
                bool optStepFiltering;
                if (string.CompareOrdinal(enableStepFiltering, "0") == 0)
                {
                    optStepFiltering = false;
                }
                else if (string.CompareOrdinal(enableStepFiltering, "1") == 0)
                {
                    optStepFiltering = true;
                }
                else
                {
                    return Constants.E_FAIL;
                }

                // TODO
                //_pollThread.RunOperation(new Operation(() => { DebuggedProcess.MICommandFactory.SetStepFiltering(optStepFiltering); }));
                return Constants.S_OK;
            }

            return Constants.E_NOTIMPL;
        }

        // Requests that all programs being debugged by this DE stop execution the next time one of their threads attempts to run.
        // This is normally called in response to the user clicking on the pause button in the debugger.
        // When the break is complete, an AsyncBreakComplete event will be sent back to the debugger.
        public int CauseBreak()
        {
            //Debug.Assert(Worker.MainThreadId == Worker.CurrentThreadId);

            return ((IDebugProgram2)this).CauseBreak();
        }

        #endregion


        #region IDebugEngineLaunch2 Members

        // Launches a process by means of the debug engine.
        // Normally, Visual Studio launches a program using the IDebugPortEx2::LaunchSuspended method and then attaches the debugger
        // to the suspended program. However, there are circumstances in which the debug engine may need to launch a program
        // (for example, if the debug engine is part of an interpreter and the program being debugged is an interpreted language),
        // in which case Visual Studio uses the IDebugEngineLaunch2::LaunchSuspended method
        // The IDebugEngineLaunch2::ResumeProcess method is called to start the process after the process has been successfully launched in a suspended state.
        public int LaunchSuspended(string pszServer, IDebugPort2 pPort, string pszExe, string pszArgs, string pszDir, string bstrEnv, string pszOptions, enum_LAUNCH_FLAGS dwLaunchFlags, uint hStdInput, uint hStdOutput, uint hStdError, IDebugEventCallback2 pCallback, out IDebugProcess2 ppProcess)
        {
            Debug.Assert(_pollThread == null);
            Debug.Assert(_engineCallback == null);
            Debug.Assert(DebuggedProcess == null);
            Debug.Assert(_ad7ProgramId == Guid.Empty);

            // Check if the logger was enabled late.
            //Logger.LoadMIDebugLogger(_configStore);

            //process = null;

            _engineCallback = new EngineCallback(this, pCallback);

            Exception exception;

            //try
            //{
                bool noDebug = dwLaunchFlags.HasFlag(enum_LAUNCH_FLAGS.LAUNCH_NODEBUG);

                // Note: LaunchOptions.GetInstance can be an expensive operation and may push a wait message loop
                //LaunchOptions launchOptions = LaunchOptions.GetInstance(_configStore, exe, args, dir, options, noDebug, _engineCallback, TargetEngine.Native, Logger);

                //StartDebugging(launchOptions);

                EngineUtils.RequireOk(pPort.GetProcess(DebuggedProcess.Id, out ppProcess));
                return Constants.S_OK;
            //}
            //catch (Exception e) when (ExceptionHelper.BeforeCatch(e, Logger, reportOnlyCorrupting: true))
            //{
            //    exception = e;
            //    // Return from the catch block so that we can let the exception unwind - the stack can get kind of big
            //}

            // If we just return the exception as an HRESULT, we will lose our message, so we instead send up an error event, and then
            // return E_ABORT.
            //OnStartDebuggingFailed(exception);

            return Constants.E_ABORT;
        }

        // Resume a process launched by IDebugEngineLaunch2.LaunchSuspended
        public int ResumeProcess(IDebugProcess2 pProcess)
        {
            Debug.Assert(_pollThread != null);
            Debug.Assert(_engineCallback != null);
            Debug.Assert(DebuggedProcess != null);
            Debug.Assert(_ad7ProgramId == Guid.Empty);

            try
            {
                //AD_PROCESS_ID processId = EngineUtils.GetProcessId(pProcess);

                //if (!EngineUtils.ProcIdEquals(processId, DebuggedProcess.Id))
                //{
                //    return Constants.S_FALSE;
                //}

                // Send a program node to the SDM. This will cause the SDM to turn around and call IDebugEngine2.Attach
                // which will complete the hookup with AD7
                //IDebugPort2 port;
                //EngineUtils.RequireOk(process.GetPort(out port));

                //IDebugDefaultPort2 defaultPort = (IDebugDefaultPort2)port;

                //IDebugPortNotify2 portNotify;
                //EngineUtils.RequireOk(defaultPort.GetPortNotify(out portNotify));

                //EngineUtils.RequireOk(portNotify.AddProgramNode(new AD7ProgramNode(_debuggedProcess.Id, _engineGuid)));

                if (_ad7ProgramId == Guid.Empty)
                {
                    Debug.Fail("Unexpected problem -- IDebugEngine2.Attach wasn't called");
                    return Constants.E_FAIL;
                }

                // NOTE: We wait for the program create event to be continued before we really resume the process

                return Constants.S_OK;
            }
            //catch (MIException e)
            //{
            //    return e.HResult;
            //}
            catch (Exception e)
            {
                return EngineUtils.UnexpectedException(e);
            }
        }

        // Determines if a process can be terminated.
        public int CanTerminateProcess(IDebugProcess2 pProcess)
        {
            Debug.Assert(_pollThread != null);
            Debug.Assert(_engineCallback != null);
            Debug.Assert(DebuggedProcess != null);

            //AD_PROCESS_ID processId = EngineUtils.GetProcessId(pProcess);

            //if (EngineUtils.ProcIdEquals(processId, DebuggedProcess.Id))
            {
                return Constants.S_OK;
            }
            //else
            {
                return Constants.S_FALSE;
            }
        }

        // This function is used to terminate a process that the SampleEngine launched
        // The debugger will call IDebugEngineLaunch2::CanTerminateProcess before calling this method.
        public int TerminateProcess(IDebugProcess2 pProcess)
        {
            Debug.Assert(_pollThread != null);
            Debug.Assert(_engineCallback != null);
            Debug.Assert(DebuggedProcess != null);

            try
            {
                int processId = EngineUtils.GetProcessId(pProcess);
                //if (processId != DebuggedProcess.Id)
                //{
                //    return Constants.S_FALSE;
                //}

                //DebuggedProcess.Terminate();

                return Constants.S_OK;
            }
            catch (ComponentException e)
            {
                return e.HResult;
            }
            catch (Exception e)
            {
                return EngineUtils.UnexpectedException(e);
            }
        }

        #endregion


        #region IDebugEngine3 Members

        public int SetSymbolPath(string szSymbolSearchPath, string szSymbolCachePath, uint Flags)
        {
            return Constants.S_OK;
        }

        public int LoadSymbols()
        {
            return Constants.S_FALSE; // indicate that we didn't load symbols for anything
        }

        public int SetJustMyCodeState(int fUpdate, uint dwModules, JMC_CODE_SPEC[] rgJMCSpec)
        {
            return Constants.S_OK;
        }

        public int SetEngineGuid(ref Guid guidEngine)
        {
            _engineGuid = guidEngine;
            // TODO
            //_configStore.SetEngineGuid(_engineGuid);
            return Constants.S_OK;
        }

        public int SetAllExceptions(enum_EXCEPTION_STATE dwState)
        {
            //DebuggedProcess?.ExceptionManager.SetAllExceptions(dwState);
            return Constants.S_OK;
        }

        #endregion


        #region IDebugProgram3 Members

        public int EnumThreads(out IEnumDebugThreads2 ppEnum)
        {
            DebuggedThread[] threads = null;
            //DebuggedProcess.WorkerThread.RunOperation(async () => threads = await DebuggedProcess.ThreadCache.GetThreads());

            AD7Thread[] threadObjects = new AD7Thread[threads.Length];
            for (int i = 0; i < threads.Length; i++)
            {
                Debug.Assert(threads[i].Client != null);
                threadObjects[i] = (AD7Thread)threads[i].Client;
            }

            //ppEnum = new Microsoft.MIDebugEngine.AD7ThreadEnum(threadObjects);
            ppEnum = null;

            return Constants.S_OK;
        }

        // Gets the name of the program.
        // The name returned by this method is always a friendly, user-displayable name that describes the program.
        public int GetName(out string pbstrName)
        {
            // The Sample engine uses default transport and doesn't need to customize the name of the program,
            // so return NULL.
            pbstrName = null;
            return Constants.S_OK;
        }

        public int GetProcess(out IDebugProcess2 ppProcess)
        {
            Debug.Fail("This function is not called by the debugger");

            ppProcess = null;
            return Constants.E_NOTIMPL;
        }

        // Terminates the program.
        public int Terminate()
        {
            // Because the sample engine is a native debugger, it implements IDebugEngineLaunch2, and will terminate
            // the process in IDebugEngineLaunch2.TerminateProcess
            return Constants.S_OK;
        }

        public int Attach(IDebugEventCallback2 pCallback)
        {
            Debug.Fail("This function is not called by the debugger");

            return Constants.E_NOTIMPL;
        }

        // Determines if a debug engine (DE) can detach from the program.
        public int CanDetach()
        {
            // The sample engine always supports detach
            return Constants.S_OK;
        }

        // Detach is called when debugging is stopped and the process was attached to (as opposed to launched)
        // or when one of the Detach commands are executed in the UI.
        public int Detach()
        {
           // Debug.Assert(Worker.MainThreadId == Worker.CurrentThreadId);

            _breakpointManager.ClearBoundBreakpoints();

            // TODO

            //try
            //{
            //    _pollThread.RunOperation(() => DebuggedProcess.CmdDetach());
            //    DebuggedProcess.Detach();
            //}
            //catch (DebuggerDisposedException)
            //{
            //    // Detach command could cause DebuggerDisposedException and we ignore that.
            //}

            return Constants.S_OK;
        }

        // Gets a GUID for this program. A debug engine (DE) must return the program identifier originally passed to the IDebugProgramNodeAttach2::OnAttach
        // or IDebugEngine2::Attach methods. This allows identification of the program across debugger components.
        public int GetProgramId(out Guid pguidProgramId)
        {
            Debug.Assert(_ad7ProgramId != Guid.Empty);

            pguidProgramId = _ad7ProgramId;
            return Constants.S_OK;
        }

        // The properties returned by this method are specific to the program. If the program needs to return more than one property,
        // then the IDebugProperty2 object returned by this method is a container of additional properties and calling the
        // IDebugProperty2::EnumChildren method returns a list of all properties.
        // A program may expose any number and type of additional properties that can be described through the IDebugProperty2 interface.
        // An IDE might display the additional program properties through a generic property browser user interface.
        // The sample engine does not support this
        public int GetDebugProperty(out IDebugProperty2 ppProperty)
        {
            throw new NotImplementedException();
        }

        public int Execute()
        {
            Debug.Fail("This function is not called by the debugger.");
            return Constants.E_NOTIMPL;
        }

        // Continue is called from the SDM when it wants execution to continue in the debugee
        // but have stepping state remain. An example is when a tracepoint is executed,
        // and the debugger does not want to actually enter break mode.
        public int Continue(IDebugThread2 pThread)
        {
            // VS Code currently isn't providing a thread Id in certain cases. Work around this by handling null values.
            AD7Thread thread = pThread as AD7Thread;

            //try
            //{
            //    if (_pollThread.IsPollThread())
            //    {
            //        _debuggedProcess.Continue(thread?.GetDebuggedThread());
            //    }
            //    else
            //    {
            //        _pollThread.RunOperation(() => _debuggedProcess.Continue(thread?.GetDebuggedThread()));
            //    }
            //}
            //catch (InvalidCoreDumpOperationException)
            //{
            //    return AD7_HRESULT.E_CRASHDUMP_UNSUPPORTED;
            //}
            //catch (Exception e)
            //{
            //    _engineCallback.OnError(EngineUtils.GetExceptionDescription(e));
            //    return Constants.E_ABORT;
            //}

            return Constants.S_OK;
        }

        // This method is deprecated. Use the IDebugProcess3::Step method instead.
        public int Step(IDebugThread2 pThread, enum_STEPKIND sk, enum_STEPUNIT Step)
        {
            return Constants.S_OK;
        }

        // Gets the name and identifier of the debug engine (DE) running this program.
        public int GetEngineInfo(out string pbstrEngine, out Guid pguidEngine)
        {
            pbstrEngine = "nanoFramework Debug Engine";
            pguidEngine = DebuggerGuids.EngineId;
            return Constants.S_OK;
        }

        // Enumerates the code contexts for a given position in a source file.
        public int EnumCodeContexts(IDebugDocumentPosition2 pDocPos, out IEnumDebugCodeContexts2 ppEnum)
        {
            string documentName;
            EngineUtils.CheckOk(pDocPos.GetFileName(out documentName));

            // Get the location in the document
            TEXT_POSITION[] startPosition = new TEXT_POSITION[1];
            TEXT_POSITION[] endPosition = new TEXT_POSITION[1];
            EngineUtils.CheckOk(pDocPos.GetRange(startPosition, endPosition));
            List<IDebugCodeContext2> codeContexts = new List<IDebugCodeContext2>();

            List<ulong> addresses = null;
            uint line = startPosition[0].dwLine + 1;
            //DebuggedProcess.WorkerThread.RunOperation(async () =>
            //{
            //    addresses = await DebuggedProcess.StartAddressesForLine(documentName, line);
            //});

            if (addresses != null && addresses.Count > 0)
            {
                foreach (var a in addresses)
                {
                    //var codeCxt = new AD7MemoryAddress(this, a, null);
                    //TEXT_POSITION pos;
                    //pos.dwLine = line;
                    //pos.dwColumn = 0;
                    //MITextPosition textPosition = new MITextPosition(documentName, pos, pos);
                    //codeCxt.SetDocumentContext(new AD7DocumentContext(textPosition, codeCxt, this.DebuggedProcess));
                    //codeContexts.Add(codeCxt);
                }
                if (codeContexts.Count > 0)
                {
                    ppEnum = new AD7CodeContextEnum(codeContexts.ToArray());
                    return Constants.S_OK;
                }
            }
            ppEnum = null;
            return Constants.E_FAIL;
        }

        // The memory bytes as represented by the IDebugMemoryBytes2 object is for the program's image in memory and not any memory
        // that was allocated when the program was executed.
        public int GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes)
        {
            ppMemoryBytes = this;
            return Constants.S_OK;
        }

        // The debugger calls this when it needs to obtain the IDebugDisassemblyStream2 for a particular code-context.
        // The sample engine does not support dissassembly so it returns E_NOTIMPL
        // In order for this to be called, the Disassembly capability must be set in the registry for this Engine
        public int GetDisassemblyStream(enum_DISASSEMBLY_STREAM_SCOPE dwScope, IDebugCodeContext2 pCodeContext, out IDebugDisassemblyStream2 ppDisassemblyStream)
        {
            // TODO
            ppDisassemblyStream = null;
            //ppDisassemblyStream = new AD7DisassemblyStream(this, dwScope, codeContext);
            return Constants.S_OK;
        }

        // EnumModules is called by the debugger when it needs to enumerate the modules in the program.
        public int EnumModules(out IEnumDebugModules2 ppEnum)
        {
            ppEnum = null;

            // TODO
            //DebuggedModule[] modules = DebuggedProcess.GetModules();

            //AD7Module[] moduleObjects = new AD7Module[modules.Length];
            //for (int i = 0; i < modules.Length; i++)
            //{
            //    moduleObjects[i] = new AD7Module(modules[i], DebuggedProcess);
            //}

            //ppEnum = new AD7ModuleEnum(moduleObjects);
            return Constants.S_OK;
        }

        // This method gets the Edit and Continue (ENC) update for this program. A custom debug engine always returns E_NOTIMPL
        public int GetENCUpdate(out object ppUpdate)
        {
            // The sample engine does not participate in managed edit & continue.
            ppUpdate = null;
            return Constants.S_OK;
        }

        // EnumCodePaths is used for the step-into specific feature -- right click on the current statment and decide which
        // function to step into. This is not something that the SampleEngine supports.
        public int EnumCodePaths(string pszHint, IDebugCodeContext2 pStart, IDebugStackFrame2 pFrame, int fSource, out IEnumCodePaths2 ppEnum, out IDebugCodeContext2 ppSafety)
        {
            ppEnum = null;
            ppSafety = null;
            return Constants.E_NOTIMPL;
        }

        // Writes a dump to a file.
        public int WriteDump(enum_DUMPTYPE DUMPTYPE, string pszDumpUrl)
        {
            // The sample debugger does not support creating or reading mini-dumps.
            return Constants.E_NOTIMPL;
        }

        public int ExecuteOnThread(IDebugThread2 pThread)
        {
            throw new NotImplementedException();
        }

        #endregion


        #region IDebugEngineProgram2 Members

        // Stops all threads running in this program.
        // This method is called when this program is being debugged in a multi-program environment. When a stopping event from some other program
        // is received, this method is called on this program. The implementation of this method should be asynchronous;
        // that is, not all threads should be required to be stopped before this method returns. The implementation of this method may be
        // as simple as calling the IDebugProgram2::CauseBreak method on this program.
        public int Stop()
        {
            // TODO
            //DebuggedProcess.WorkerThread.RunOperation(async () =>
            //{
            //    await _debuggedProcess.CmdBreak(MICore.Debugger.BreakRequest.Stop);
            //});
            //return _debuggedProcess.ProcessState == ProcessState.Running ? Constants.S_ASYNC_STOP : Constants.S_OK;
            return Constants.S_OK;
        }

        // WatchForThreadStep is used to cooperate between two different engines debugging the same process.
        // The sample engine doesn't cooperate with other engines, so it has nothing to do here.
        public int WatchForThreadStep(IDebugProgram2 pOriginatingProgram, uint dwTid, int fWatch, uint dwFrame)
        {
            return Constants.S_OK;
        }

        // WatchForExpressionEvaluationOnThread is used to cooperate between two different engines debugging
        // the same process. The sample engine doesn't cooperate with other engines, so it has nothing
        // to do here.
        public int WatchForExpressionEvaluationOnThread(IDebugProgram2 pOriginatingProgram, uint dwTid, uint dwEvalFlags, IDebugEventCallback2 pExprCallback, int fWatch)
        {
            return Constants.S_OK;
        }

        #endregion


        #region IDebugMemoryBytes2 Members

        public int ReadAt(IDebugMemoryContext2 pStartContext, uint dwCount, byte[] rgbMemory, out uint pdwRead, ref uint pdwUnreadable)
        {
            pdwUnreadable = 0;
            AD7MemoryAddress addr = (AD7MemoryAddress)pStartContext;
            uint bytesRead = 0;
            int hr = Constants.S_OK;
            //DebuggedProcess.WorkerThread.RunOperation(async () =>
            //{
            //    bytesRead = await DebuggedProcess.ReadProcessMemory(addr.Address, dwCount, rgbMemory);
            //});

            if (bytesRead == uint.MaxValue)
            {
                bytesRead = 0;
            }

            if (bytesRead < dwCount) // copied from Concord
            {
                // assume 4096 sized pages: ARM has 4K or 64K pages
                uint pageSize = 4096;
                // TODO
                //ulong readEnd = addr.Address + bytesRead;
                //ulong nextPageStart = (readEnd + pageSize - 1) / pageSize * pageSize;
                //if (nextPageStart == readEnd)
                //{
                //    nextPageStart = readEnd + pageSize;
                //}
                //// if we have crossed a page boundry - Unreadable = bytes till end of page
                //uint maxUnreadable = dwCount - bytesRead;
                //if (addr.Address + dwCount > nextPageStart)
                //{
                //    pdwUnreadable = (uint)Math.Min(maxUnreadable, nextPageStart - readEnd);
                //}
                //else
                //{
                //    pdwUnreadable = (uint)Math.Min(maxUnreadable, pageSize);
                //}
            }
            pdwRead = bytesRead;
            return hr;
        }

        public int WriteAt(IDebugMemoryContext2 pStartContext, uint dwCount, byte[] rgbMemory)
        {
            throw new NotImplementedException();
        }

        public int GetSize(out ulong pqwSize)
        {
            throw new NotImplementedException();
        }

        #endregion


        #region IDebugEngine110 Members

        public int SetMainThreadSettingsCallback110(IDebugSettingsCallback110 pCallback)
        {
            // TODO
            //_settingsCallback = pCallback;
            return Constants.S_OK;
        }

        #endregion

    }
}
