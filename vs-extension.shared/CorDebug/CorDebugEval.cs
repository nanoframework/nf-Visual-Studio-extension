//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugEval : ICorDebugEval, ICorDebugEval2
    {
        const int SCRATCH_PAD_INDEX_NOT_INITIALIZED = -1;

        bool _fActive;
        CorDebugThread _threadReal;
        CorDebugThread _threadVirtual;
        CorDebugAppDomain _appDomain;
        int _iScratchPad;
        CorDebugValue _resultValue;
        EvalResult _resultType;
        bool _fException;

        public enum EvalResult
        {
            NotFinished,
            Complete,
            Abort,
            Exception,
        }
        
        public CorDebugEval (CorDebugThread thread) 
        {
            _appDomain = thread.Chain.ActiveFrame.AppDomain;
            _threadReal = thread;
            _resultType = EvalResult.NotFinished;
            ResetScratchPadLocation ();            
        }

        public CorDebugThread ThreadVirtual
        {
            get { return _threadVirtual; }
        }

        public CorDebugThread ThreadReal
        {
            get { return _threadReal; }
        }

        public Engine Engine
        {
            get { return Process.Engine; }
        }

        public CorDebugProcess Process
        {
            get { return _threadReal.Process; }
        }

        public CorDebugAppDomain AppDomain
        {
            get { return _appDomain; }
        }

        private CorDebugValue GetResultValue ()
        {
            if (_resultValue == null)
            {
                _resultValue = Process.ScratchPad.GetValue (_iScratchPad, _appDomain);
            }

            return _resultValue;
        }

        private int GetScratchPadLocation ()
        {
            if (_iScratchPad == SCRATCH_PAD_INDEX_NOT_INITIALIZED)
            {
                _iScratchPad = Process.ScratchPad.ReserveScratchBlock ();
            }

            return _iScratchPad;
        }

        private void ResetScratchPadLocation()
        {
            _iScratchPad = SCRATCH_PAD_INDEX_NOT_INITIALIZED;
            _resultValue = null;            
        }

        public void StoppedOnUnhandledException()
        {
            /*
             * store the fact that this eval ended with an exception
             * the exception will be stored in the scratch pad by nanoCLR
             * but the event to cpde should wait to be queued until the eval
             * thread completes.  At that time, the information is lost as to
             * the fact that the result was an exception (rather than the function returning
             * an object of type exception. Hence, this flag.
            */
            _fException = true;
        }

        public void EndEval (EvalResult resultType, bool fSynchronousEval)
        {
            try
            {
                //This is used to avoid deadlock.  Suspend commands synchronizes on this.Process                
                Process.SuspendCommands(true);

                Debug.Assert(Utility.FImplies(fSynchronousEval, !_fActive));

                if (fSynchronousEval || _fActive)  //what to do if the eval isn't active anymore??
                {
                    bool fKillThread = false;

                    if (_threadVirtual != null)
                    {
                        if (_threadReal.GetLastCorDebugThread() != _threadVirtual)
                            throw new ArgumentException();

                        _threadReal.RemoveVirtualThread(_threadVirtual);
                    }

                    //Stack frames don't appear if they are not refreshed
                    if (fSynchronousEval)
                    {
                        for (CorDebugThread thread = _threadReal; thread != null; thread = thread.NextThread)
                        {
                            thread.RefreshChain();
                        }
                    }

                    if(_fException)
                    {
                        resultType = EvalResult.Exception;
                    }

                    //Check to see if we are able to EndEval -- is this the last virtual thread?
                    _fActive = false;
                    _resultType = resultType;
                    switch (resultType)
                    {
                        case EvalResult.Complete:
                            Process.EnqueueEvent(new ManagedCallbacks.ManagedCallbackEval(_threadReal, this, ManagedCallbacks.ManagedCallbackEval.EventType.EvalComplete));
                            break;

                        case EvalResult.Exception:
                            Process.EnqueueEvent(new ManagedCallbacks.ManagedCallbackEval(_threadReal, this, ManagedCallbacks.ManagedCallbackEval.EventType.EvalException));                             
                            break;

                        case EvalResult.Abort:
                            fKillThread = true;
                            /* WARNING!!!!
                             * If we do not give VS a EvalComplete message within 3 seconds of them calling ICorDebugEval::Abort then VS will attempt a RudeAbort
                             * and will display a scary error message about a serious internal debugger error and ignore all future debugging requests, among other bad things.
                             */
                            Process.EnqueueEvent(new ManagedCallbacks.ManagedCallbackEval(_threadReal, this, ManagedCallbacks.ManagedCallbackEval.EventType.EvalComplete));
                            break;
                    }

                    if (fKillThread && _threadVirtual != null)
                    {
                        Engine.KillThread(_threadVirtual.ID);
                    }

                    if (resultType == EvalResult.Abort)
                    {
                        Process.PauseExecution();
                    }
                }
            }
            finally
            {
                Process.SuspendCommands(false);
            }
        }

        private uint GetTypeDef_Index(CorElementType elementType, ICorDebugClass pElementClass)
        {
            uint tdIndex;

            if (pElementClass != null)
            {
                tdIndex = ((CorDebugClass)pElementClass).TypeDef_Index;
            }
            else
            {
                CorDebugProcess.BuiltinType builtInType = Process.ResolveBuiltInType(elementType);
                tdIndex = builtInType.GetClass(_appDomain).TypeDef_Index;
            }

            return tdIndex;
        }
        
        #region ICorDebugEval Members

        int ICorDebugEval.IsActive (out int pbActive)
        {
            pbActive = Boolean.BoolToInt (_fActive);

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.GetThread( out ICorDebugThread ppThread )
        {
            ppThread = _threadReal;

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.GetResult( out ICorDebugValue ppResult )
        {
            switch (_resultType)
            {
                case EvalResult.Exception:
                case EvalResult.Complete:
                    ppResult = GetResultValue ();
                    break;
                default:
                    ppResult = null;
                    throw new ArgumentException ();
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.NewArray(CorElementType elementType, ICorDebugClass pElementClass, uint rank, ref uint dims, ref uint lowBounds)
        {         
            if(rank != 1) return COM_HResults.E_FAIL;

            Process.SetCurrentAppDomain(AppDomain);
            uint tdIndex = GetTypeDef_Index(elementType, pElementClass);
            Engine.AllocateArray(GetScratchPadLocation(), tdIndex, 1, (int)dims);
            EndEval (EvalResult.Complete, true);

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.CallFunction( ICorDebugFunction pFunction, uint nArgs, ICorDebugValue[] ppArgs )
        {
            try
            {
                //CreateThread will cause a thread create event to occur.  This is a virtual thread, so 
                //we need to suspend processing of nanoCLR commands until we have created the thread ourselves
                //and the processing of a new virtual thread will be ignored.
                Process.SuspendCommands (true);

                //need to flush the breakpoints in case new breakpoints were waiting until process was resumed.
                Process.UpdateBreakpoints ();   

                Debug.Assert (nArgs == ppArgs.Length);
                Debug.Assert (Process.IsExecutionPaused);

                CorDebugFunction function = (CorDebugFunction)pFunction;

                uint md = function.MethodDef_Index;
                if(function.IsVirtual && function.IsInstance)
                {
                    Debug.Assert(nArgs > 0);
                    
                    md = Engine.GetVirtualMethod(function.MethodDef_Index, ((CorDebugValue)ppArgs[0]).RuntimeValue);
                }

                Process.SetCurrentAppDomain(AppDomain);

                //Send the selected thread ID to the device so calls that use Thread.CurrentThread work as the user expects.
                uint pid = Engine.CreateThread(md, GetScratchPadLocation(), _threadReal.ID);

                if (pid == uint.MaxValue)
                {
                    throw new ArgumentException("nanoCLR cannot call this function.  Possible reasons include: ByRef arguments not supported");
                }

                //If anything below fails, we need to clean up by killing the thread
                if (nArgs > 0)
                {
                    List<RuntimeValue> stackFrameValues = Engine.GetStackFrameValueAll(pid, 0, function.NumArg, Engine.StackValueKind.Argument);

                    for (int iArg = 0; iArg < nArgs; iArg++)
                    {
                        CorDebugValue valueSource = (CorDebugValue)ppArgs[iArg];
                        CorDebugValue valueDestination = CorDebugValue.CreateValue (stackFrameValues[iArg], _appDomain);

                        if (valueDestination.RuntimeValue.Assign(valueSource.RuntimeValue) == null)
                        {
                            throw new ArgumentException("nanoCLR cannot set argument " + iArg);
                        }
                    }
                }

                _threadVirtual = new CorDebugThread (Process, pid, this);
                _threadReal.AttachVirtualThread (_threadVirtual);
                Debug.Assert (!_fActive);
                _fActive = true;                

                //It is possible that a hard breakpoint is hit, the first line of the function
                //to evaluate.  If that is the case, than breakpoints need to be drained so the 
                //breakpoint event is fired, to avoid a race condition, where cpde resumes 
                //execution to start the function eval before it gets the breakpoint event
                //This is primarily due to the difference in behaviour of the nanoCLR and the desktop.
                //In the desktop, the hard breakpoint will not get hit until execution is resumed.
                //The nanoCLR can hit the breakpoint during the Thread_Create call.
                
                Process.DrainBreakpoints();
            }
            finally
            {
                Process.SuspendCommands (false);    
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.NewString( string @string )
        {
            Process.SetCurrentAppDomain(AppDomain);
            
            //changing strings is dependant on this method working....
            Engine.AllocateString(GetScratchPadLocation(), @string);
            EndEval (EvalResult.Complete, true);

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.NewObjectNoConstructor( ICorDebugClass pClass )
        {
            Process.SetCurrentAppDomain(AppDomain);
            Engine.AllocateObject(GetScratchPadLocation (), ((CorDebugClass)pClass).TypeDef_Index);
            EndEval (EvalResult.Complete, true);

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.CreateValue(CorElementType elementType, ICorDebugClass pElementClass, out ICorDebugValue ppValue)
        {
            uint tdIndex = GetTypeDef_Index(elementType, pElementClass);
            Debug.Assert(Utility.FImplies(pElementClass != null, elementType == CorElementType.ELEMENT_TYPE_VALUETYPE));

            Process.SetCurrentAppDomain(AppDomain);
            Engine.AllocateObject(GetScratchPadLocation (), tdIndex);

            ppValue = GetResultValue ();
            ResetScratchPadLocation ();

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.NewObject( ICorDebugFunction pConstructor, uint nArgs, ICorDebugValue[] ppArgs )
        {
            Debug.Assert (nArgs == ppArgs.Length);

            CorDebugFunction f = (CorDebugFunction)pConstructor;
            CorDebugClass c = f.Class;

            Process.SetCurrentAppDomain(AppDomain);
            Engine.AllocateObject(GetScratchPadLocation (), c.TypeDef_Index);
            
            ICorDebugValue[] args = new ICorDebugValue[nArgs + 1];

            args[0] = GetResultValue ();
            ppArgs.CopyTo (args, 1);
            ((ICorDebugEval)this).CallFunction (pConstructor, (uint)args.Length, args);

            return COM_HResults.S_OK;
        }

        int ICorDebugEval.Abort ()
        {
            EndEval (EvalResult.Abort, false);

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugEval2 Members

        int ICorDebugEval2.CallParameterizedFunction(ICorDebugFunction pFunction, uint nTypeArgs, ICorDebugType[] ppTypeArgs, uint nArgs, ICorDebugValue[] ppArgs)
        {
            return ((ICorDebugEval)this).CallFunction(pFunction, nArgs, ppArgs);
        }

        int ICorDebugEval2.CreateValueForType(ICorDebugType pType, out ICorDebugValue ppValue)
        {
            CorElementType type;
            ICorDebugClass cls;

            pType.GetType( out type );
            pType.GetClass( out cls );

            return ((ICorDebugEval)this).CreateValue(type, cls, out ppValue);
        }

        int ICorDebugEval2.NewParameterizedObject(ICorDebugFunction pConstructor, uint nTypeArgs, ICorDebugType[] ppTypeArgs, uint nArgs, ICorDebugValue[] ppArgs)
        {
            return ((ICorDebugEval)this).NewObject(pConstructor, nArgs, ppArgs);
        }

        int ICorDebugEval2.NewParameterizedObjectNoConstructor(ICorDebugClass pClass, uint nTypeArgs, ICorDebugType[] ppTypeArgs)
        {
            return ((ICorDebugEval)this).NewObjectNoConstructor(pClass);
        }

        int ICorDebugEval2.NewParameterizedArray(ICorDebugType pElementType, uint rank, ref uint dims, ref uint lowBounds)
        {
            CorElementType type;
            ICorDebugClass cls;

            pElementType.GetType(out type);
            pElementType.GetClass(out cls);

            return ((ICorDebugEval)this).NewArray(type, cls, rank, dims, lowBounds);
        }

        int ICorDebugEval2.NewStringWithLength(string @string, uint uiLength)
        {
            string strVal = @string.Substring(0, (int)uiLength);
            
            return ((ICorDebugEval)this).NewString(strVal);
        }

        int ICorDebugEval2.RudeAbort()
        {
            return ((ICorDebugEval)this).Abort();
        }

        #endregion
    }
}
