using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.Debugger.Interop;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    // This class implements IDebugThread2 which represents a thread running in a program.
    internal class AD7Thread : IDebugThread2
    {
        readonly AD7Engine _engine;
        readonly DebuggedThread _debuggedThread;
        const string ThreadNameString = "Sample Engine Thread";
        private string _threadDisplayName;
        private uint _threadFlags;

        public AD7Thread(AD7Engine engine, DebuggedThread debuggedThread)
        {
            _engine = engine;
            _debuggedThread = debuggedThread;
        }

        ThreadContext GetThreadContext()
        {
            //ThreadContext threadContext = _engine.DebuggedProcess.GetThreadContext(_debuggedThread.Handle);
            ThreadContext threadContext = null;
            
            // TODO
            //_engine.DebuggedProcess.WorkerThread.RunOperation(async () => threadContext = await _engine.DebuggedProcess.ThreadCache.GetThreadContext(_debuggedThread));

            return threadContext;
        }

        string GetCurrentLocation(bool fIncludeModuleName)
        {
            // TODO
            //uint ip = GetThreadContext().eip;
            string location = "";// _engine.GetAddressDescription(ip);

            return location;
        }

        internal DebuggedThread GetDebuggedThread()
        {
            return _debuggedThread;
        }

        #region IDebugThread2 Members

        // Determines whether the next statement can be set to the given stack frame and code context.
        // The sample debug engine does not support set next statement, so S_FALSE is returned.
        int IDebugThread2.CanSetNextStatement(IDebugStackFrame2 stackFrame, IDebugCodeContext2 codeContext)
        {
            return Constants.S_FALSE; 
        }

        // Retrieves a list of the stack frames for this thread.
        // For the sample engine, enumerating the stack frames requires walking the callstack in the debuggee for this thread
        // and coverting that to an implementation of IEnumDebugFrameInfo2. 
        // Real engines will most likely want to cache this information to avoid recomputing it each time it is asked for,
        // and or construct it on demand instead of walking the entire stack.
        int IDebugThread2.EnumFrameInfo(enum_FRAMEINFO_FLAGS dwFieldSpec, uint nRadix, out IEnumDebugFrameInfo2 enumObject)
        {
            // TODO
            //// Ask the lower-level to perform a stack walk on this thread
            //_engine.DebuggedProcess.DoStackWalk(this._debuggedThread);
            enumObject = null;

            //try
            //{
            //    System.Collections.Generic.List<ThreadContext> stackFrames = this._debuggedThread.StackFrames;
            //    int numStackFrames = stackFrames.Count;
            //    FRAMEINFO[] frameInfoArray;

            //    if (numStackFrames == 0)
            //    {
            //        // failed to walk any frames. Only return the top frame.
            //        frameInfoArray = new FRAMEINFO[1];
            //        AD7StackFrame frame = new AD7StackFrame(_engine, this, GetThreadContext());
            //        frame.SetFrameInfo(dwFieldSpec, out frameInfoArray[0]);
            //    }
            //    else
            //    {
            //        frameInfoArray = new FRAMEINFO[numStackFrames];

            //        for (int i = 0; i < numStackFrames; i++)
            //        {
            //            AD7StackFrame frame = new AD7StackFrame(_engine, this, stackFrames[i]);
            //            frame.SetFrameInfo(dwFieldSpec, out frameInfoArray[i]);
            //        }
            //    }

            //    enumObject = new AD7FrameInfoEnum(frameInfoArray);
            //    return Constants.S_OK;
            //}
            //catch (Exception e)
            //{
            //    return EngineUtils.UnexpectedException(e);
            //} 
            return Constants.S_OK;
        }

        // Get the name of the thread. For the sample engine, the name of the thread is always "Sample Engine Thread"
        int IDebugThread2.GetName(out string threadName)
        {
            threadName = ThreadNameString;
            return Constants.S_OK;
        }

        // Return the program that this thread belongs to.
        int IDebugThread2.GetProgram(out IDebugProgram2 program)
        {
            program = _engine;
            return Constants.S_OK;
        }

        // Gets the system thread identifier.
        int IDebugThread2.GetThreadId(out uint threadId)
        {
            threadId = (uint)_debuggedThread.Id;
            return Constants.S_OK;
        }

        // Gets properties that describe a thread.
        int IDebugThread2.GetThreadProperties(enum_THREADPROPERTY_FIELDS dwFields, THREADPROPERTIES[] propertiesArray)
        {
            try
            {
                THREADPROPERTIES props = new THREADPROPERTIES();

                if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_ID) != 0)
                {
                    props.dwThreadId = (uint)_debuggedThread.Id;
                    props.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_ID;
                }
                if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_SUSPENDCOUNT) != 0) 
                {
                    // sample debug engine doesn't support suspending threads
                    props.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_SUSPENDCOUNT;
                }
                if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_STATE) != 0) 
                {
                    props.dwThreadState = (uint)enum_THREADSTATE.THREADSTATE_RUNNING;
                    props.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_STATE;
                }
                if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_PRIORITY) != 0) 
                {
                    props.bstrPriority = "Normal";
                    props.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_PRIORITY;
                }
                if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_NAME) != 0)
                {
                    props.bstrName = ThreadNameString;
                    props.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_NAME;
                }
                if ((dwFields & enum_THREADPROPERTY_FIELDS.TPF_LOCATION) != 0)
                {
                    props.bstrLocation = GetCurrentLocation(true);
                    props.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_LOCATION;
                }

                return Constants.S_OK;
            }
            catch (Exception e)
            {
                return EngineUtils.UnexpectedException(e);
            }
        }

        // Resume a thread.
        // This is called when the user chooses "Unfreeze" from the threads window when a thread has previously been frozen.
        int IDebugThread2.Resume(out uint suspendCount)
        {
            // The sample debug engine doesn't support suspending/resuming threads
            suspendCount = 0;
            return Constants.E_NOTIMPL;
        }

        // Sets the next statement to the given stack frame and code context.
        // The sample debug engine doesn't support set next statment
        int IDebugThread2.SetNextStatement(IDebugStackFrame2 stackFrame, IDebugCodeContext2 codeContext)
        {
            return Constants.E_NOTIMPL;
        }

        // suspend a thread.
        // This is called when the user chooses "Freeze" from the threads window
        int IDebugThread2.Suspend(out uint suspendCount)
        {
            // The sample debug engine doesn't support suspending/resuming threads
            suspendCount = 0;
            return Constants.E_NOTIMPL;
        }

        #endregion

        #region Uncalled interface methods
        // These methods are not currently called by the Visual Studio debugger, so they don't need to be implemented

        int IDebugThread2.GetLogicalThread(IDebugStackFrame2 stackFrame, out IDebugLogicalThread2 logicalThread)
        {
            Debug.Fail("This function is not called by the debugger");

            logicalThread = null;
            return Constants.E_NOTIMPL;
        }

        int IDebugThread2.SetThreadName(string name)
        {
            Debug.Fail("This function is not called by the debugger");

            return Constants.E_NOTIMPL;
        }

        #endregion
    }
}
