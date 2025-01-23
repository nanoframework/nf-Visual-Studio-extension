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
    // This Guid needs to match CorDebugCLSID
    [ComVisible(true), Guid("1031CDC5-1845-4930-963D-0013FE18F23B")]
    public class CorDebug : ICorDebug, IDebugRemoteCorDebug
    {
        public const string EngineId = "B9FBFF29-0842-4F5D-82CE-F38C0B3C1F3E";

        // This Guid needs to match EngineGuid
        public static Guid EngineGuid => new Guid(EngineId);

        private ArrayList _processes;
        private ICorDebugManagedCallback _callback;

        public CorDebug()
        {
            //System.Windows.Forms.MessageBox.Show("Hello from nF DE!");
            _processes = new ArrayList(1);
        }

        public ICorDebugManagedCallback ManagedCallback
        {
            [System.Diagnostics.DebuggerHidden]
            get { return _callback; }
        }

        public void RegisterProcess(CorDebugProcess process)
        {
            _processes.Add(process);
        }

        public void UnregisterProcess(CorDebugProcess process)
        {
            _processes.Remove(process);
        }

        #region ICorDebug Members

        int ICorDebug.Terminate()
        {
            // CorDebug.Terminate is not implemented
            return COM_HResults.S_OK;
        }

        int ICorDebug.GetProcess(uint dwProcessId, out ICorDebugProcess ppProcess)
        {
            ppProcess = null;

            foreach (CorDebugProcess process in _processes)
            {
                uint id = process.PhysicalProcessId.dwProcessId;

                if (dwProcessId == id)
                {
                    ppProcess = process;
                    break;
                }
            }

            return COM_HResults.BOOL_TO_HRESULT_FAIL( ppProcess != null ); /*better failure?*/
        }

        int ICorDebug.SetManagedHandler( ICorDebugManagedCallback pCallback )
        {
            _callback = pCallback;

            return COM_HResults.S_OK;
        }

        int ICorDebug.EnumerateProcesses( out ICorDebugProcessEnum ppProcess )
        {
            ppProcess = new CorDebugEnum(_processes, typeof(ICorDebugProcess), typeof(ICorDebugProcessEnum));
            return COM_HResults.S_OK;
        }

        int ICorDebug.SetUnmanagedHandler( ICorDebugUnmanagedCallback pCallback )
        {
            // CorDebug.SetUnmanagedHandler is not implemented

            return COM_HResults.S_OK;
        }

        int ICorDebug.DebugActiveProcess( uint id, int win32Attach, out ICorDebugProcess ppProcess )
        {
            ppProcess = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebug.CreateProcess( string lpApplicationName, string lpCommandLine, _SECURITY_ATTRIBUTES lpProcessAttributes, _SECURITY_ATTRIBUTES lpThreadAttributes, int bInheritHandles, uint dwCreationFlags, System.IntPtr lpEnvironment, string lpCurrentDirectory, _STARTUPINFO lpStartupInfo, _PROCESS_INFORMATION lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags, out ICorDebugProcess ppProcess )
        {
            ppProcess = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebug.CanLaunchOrAttach( uint dwProcessId, int win32DebuggingEnabled )
        {
            return COM_HResults.S_OK;
        }

        int ICorDebug.Initialize()
        {
            return COM_HResults.S_OK;
        }

        #endregion

        #region IDebugRemoteCorDebug Members

        int IDebugRemoteCorDebug.CreateProcessEx( Microsoft.VisualStudio.Debugger.Interop.IDebugPort2 pPort, string lpApplicationName, string lpCommandLine, System.IntPtr lpProcessAttributes, System.IntPtr lpThreadAttributes, int bInheritHandles, uint dwCreationFlags, System.IntPtr lpEnvironment, string lpCurrentDirectory, ref CorDebugInterop._STARTUPINFO lpStartupInfo, ref CorDebugInterop._PROCESS_INFORMATION lpProcessInformation, uint debuggingFlags, out object ppProcess )
        {
            ppProcess = null;

            try
            {
                // CreateProcessEx() is guaranteed to return a valid process object, or throw an exception
                CorDebugProcess process = CorDebugProcess.CreateProcessEx(pPort, lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags, lpEnvironment, lpCurrentDirectory, ref lpStartupInfo, ref lpProcessInformation, debuggingFlags);

                // StartDebugging() will either get a connected device into a debuggable state and start the dispatch thread, or throw.
                process.StartDebugging(this, true);
                ppProcess = process;

                return COM_HResults.S_OK;
            }
            catch (ProcessExitException)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.InitializeProcessFailedProcessDied);
                return COM_HResults.S_FALSE;
            }
            catch (Exception ex)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.InitializeProcessFailed);
                MessageCentre.InternalErrorMessage(false, ex.Message);
                return COM_HResults.S_FALSE;
            }
        }

        int IDebugRemoteCorDebug.DebugActiveProcessEx( IDebugPort2 pPort, uint id, int win32Attach, out object ppProcess )
        {
            ppProcess = null;
            try
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.Attach);
                AD_PROCESS_ID pid = new AD_PROCESS_ID();

                pid.ProcessIdType = (uint) AD_PROCESS_ID_TYPE.AD_PROCESS_ID_SYSTEM;

                pid.dwProcessId = id;

                IDebugProcess2 iDebugProcess;
                pPort.GetProcess(pid, out iDebugProcess);

                CorDebugProcess process = (CorDebugProcess) iDebugProcess;

                // StartDebugging() will either get a connected device into a debuggable state and start the dispatch thread, or throw.
                process.StartDebugging(this, false);
                ppProcess = process;

                return COM_HResults.S_OK;
            }
            catch (ProcessExitException)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.AttachFailedProcessDied);
                return COM_HResults.S_FALSE;
            }
            catch (Exception ex)
            {
                MessageCentre.DebugMessage(Resources.ResourceStrings.AttachFailed);
                MessageCentre.InternalErrorMessage(false, ex.Message);
                return COM_HResults.S_FALSE;
            }
       }

       #endregion
    }
}
