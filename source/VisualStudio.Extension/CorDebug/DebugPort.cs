//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using nanoFramework.Tools.Debugger;
using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class DebugPort : IDebugPort2, IDebugPortEx2, IDebugPortNotify2, IConnectionPointContainer
    {                        
        private Guid _guid;
        private ArrayList _alProcesses;
        private string _name;
        private ConnectionPoint _cpDebugPortEvents2;
        //This can't be shared with other debugPorts, for remote attaching to multiple processes....???
        protected uint _pidNext;

        public NanoDeviceBase Device { get; protected set; }

        public DebugPort(NanoDeviceBase device, DebugPortSupplier portSupplier)
        {
            _name = device.Description;
            DebugPortSupplier = portSupplier;
            Device = device;
            _cpDebugPortEvents2 = new ConnectionPoint(this, typeof(IDebugPortEvents2).GUID);
            _alProcesses = ArrayList.Synchronized(new ArrayList(1));
            _pidNext = 1;
            _guid = Guid.NewGuid();
        }

        public bool ContainsProcess(CorDebugProcess process)
        {
            return _alProcesses.Contains(process);
        }

        public void RefreshProcesses()
        {
            ArrayList processes = new ArrayList(_alProcesses.Count + NanoFrameworkPackage.NanoDeviceCommService.DebugClient.NanoFrameworkDevices.Count);

            foreach (NanoDeviceBase device in NanoFrameworkPackage.NanoDeviceCommService.DebugClient.NanoFrameworkDevices)
            {
                CorDebugProcess process = EnsureProcess(device);

                processes.Add(process);
            }

            for (int i = _alProcesses.Count - 1; i >= 0; i--)
            {
                CorDebugProcess process = (CorDebugProcess)_alProcesses[i];

                if (!processes.Contains(process))
                {
                    RemoveProcess(process);
                }
            }
        }

        public DebugPortSupplier DebugPortSupplier { [System.Diagnostics.DebuggerHidden]get; protected set; }

        public void AddProcess(CorDebugProcess process)
        {
            if(!_alProcesses.Contains( process ))
            {
                _alProcesses.Add( process );
            }
        }

        public CorDebugProcess EnsureProcess(NanoDeviceBase device)
        {
            CorDebugProcess process = ProcessFromPortDefinition(device);            

            if (process == null)
            {
                process = new CorDebugProcess(this, device);

                uint pid = _pidNext++;

                process.SetPid(pid);

                AddProcess(process);
            }

            return process;
        }

        private void RemoveProcess(CorDebugProcess process)
        {
            ((ICorDebugProcess)process).Terminate(0);
            _alProcesses.Remove(process);
        }

        public void RemoveProcess(NanoDeviceBase device)
        {
            CorDebugProcess process = ProcessFromPortDefinition(device);

            if (process != null)
            {
                RemoveProcess(process);
            }
        }

        public bool AreProcessIdEqual(AD_PROCESS_ID pid1, AD_PROCESS_ID pid2)
        {
            if (pid1.ProcessIdType != pid2.ProcessIdType)
                return false;

            switch ((AD_PROCESS_ID_TYPE) pid1.ProcessIdType)
            {
                case AD_PROCESS_ID_TYPE.AD_PROCESS_ID_SYSTEM:
                    return pid1.dwProcessId == pid2.dwProcessId;
                case AD_PROCESS_ID_TYPE.AD_PROCESS_ID_GUID:
                    return Guid.Equals(pid1.guidProcessId, pid2.guidProcessId);
                default:
                    return false;
            }
        }

        public Guid PortId
        {
            get { return _guid; }
        }

        public string Name
        {
            [System.Diagnostics.DebuggerHidden]
            get { return _name; }
        }
        
        public CorDebugProcess GetDeviceProcess(string deviceName, int eachSecondRetryMaxCount)
        {
            if (string.IsNullOrEmpty(deviceName))
                throw new Exception("DebugPort.GetDeviceProcess() called with no argument");

            NanoFrameworkPackage.MessageCentre.StartProgressMessage(String.Format(Resources.ResourceStrings.StartDeviceSearch, deviceName, eachSecondRetryMaxCount));

            CorDebugProcess process = InternalGetDeviceProcess(deviceName);
            if (process != null)
                return process;

            if (eachSecondRetryMaxCount < 0) eachSecondRetryMaxCount = 0;

            for (int i = 0; i < eachSecondRetryMaxCount && process == null; i++)
            {
                System.Threading.Thread.Sleep(1000);
                process = InternalGetDeviceProcess(deviceName);
            }

            NanoFrameworkPackage.MessageCentre.StopProgressMessage(String.Format((process == null) 
                                                                    ? Resources.ResourceStrings.DeviceFound
                                                                    : Resources.ResourceStrings.DeviceNotFound,
                                                                  deviceName));
            return process;            
        }
        
        public CorDebugProcess GetDeviceProcess(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                throw new Exception("DebugPort.GetDeviceProcess() called with no argument");
                            
            return InternalGetDeviceProcess(deviceName);
        }

        private CorDebugProcess InternalGetDeviceProcess(string deviceName)
        {
            CorDebugProcess process = null;

            RefreshProcesses();
            
            for(int i = 0; i < _alProcesses.Count; i++)
            {
                CorDebugProcess processT = (CorDebugProcess)_alProcesses[i];
                NanoDeviceBase device = processT.Device;
                if (String.Compare(GetDeviceName(device), deviceName, true) == 0)
                {
                    process = processT;
                    break;
                }
            }

            // TODO
            //if(m_portFilter == PortFilter.TcpIp && process == null)
            //{
            //    process = EnsureProcess(PortDefinition.CreateInstanceForTcp(deviceName));                
            //}
                            
            return process;
        }

        public string GetDeviceName(NanoDeviceBase device)
        {
            return device.Description;
        }

        private bool ArePortEntriesEqual(NanoDeviceBase device1, NanoDeviceBase device2)
        {
            if (device1.Description != device2.Description) 
                return false;
            
            if (device1.GetType() != device2.GetType()) 
                return false;
                      
            return true;
        }

        private CorDebugProcess ProcessFromPortDefinition(NanoDeviceBase device)
        {
            foreach (CorDebugProcess process in _alProcesses)
            {
                if (ArePortEntriesEqual(device, process.Device))
                    return process;
            }

            return null;
        }
        
        private CorDebugProcess GetProcess( AD_PROCESS_ID ProcessId )
        {                        
            AD_PROCESS_ID pid;                   

            foreach (CorDebugProcess process in _alProcesses)
            {
                pid = process.PhysicalProcessId;

                if (AreProcessIdEqual(ProcessId, pid))
                {
                    return process;
                }
            }

            return null;
        }
                
        private CorDebugProcess GetProcess( IDebugProgramNode2 programNode )
        {
            AD_PROCESS_ID[] pids = new AD_PROCESS_ID[1];                        

            programNode.GetHostPid( pids );
            return GetProcess( pids[0] );    
        }

        private CorDebugAppDomain GetAppDomain( IDebugProgramNode2 programNode )
        {
            uint appDomainId;

            CorDebugProcess process = GetProcess( programNode );

            IDebugCOMPlusProgramNode2 node = (IDebugCOMPlusProgramNode2)programNode;
            node.GetAppDomainId( out appDomainId );

            CorDebugAppDomain appDomain = process.GetAppDomainFromId( appDomainId );

            return appDomain;
        }

        private void SendProgramEvent(IDebugProgramNode2 programNode, enum_EVENTATTRIBUTES attributes, Guid iidEvent)
        {
            CorDebugProcess process = GetProcess( programNode );
            CorDebugAppDomain appDomain = GetAppDomain( programNode );

            IDebugEvent2 evt = new DebugEvent((uint) attributes);
            foreach (IDebugPortEvents2 dpe in _cpDebugPortEvents2.Sinks)
            {
                dpe.Event(DebugPortSupplier.CoreServer, this, (IDebugProcess2)process, (IDebugProgram2) appDomain, evt, ref iidEvent);
            }
        }

        #region IDebugPort2 Members

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPort2.GetPortId(out Guid pguidPort)
        {
            pguidPort = PortId;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPort2.GetProcess(AD_PROCESS_ID ProcessId, out IDebugProcess2 ppProcess)
        {
            ppProcess = ((DebugPort)this).GetProcess( ProcessId );

            return COM_HResults.BOOL_TO_HRESULT_FAIL( ppProcess != null );            
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPort2.GetPortSupplier(out IDebugPortSupplier2 ppSupplier)
        {
            ppSupplier = DebugPortSupplier;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPort2.GetPortRequest(out IDebugPortRequest2 ppRequest)
        {
            ppRequest = null;
            return COM_HResults.E_NOTIMPL;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPort2.EnumProcesses(out IEnumDebugProcesses2 ppEnum)
        {
            RefreshProcesses();
            ppEnum = new CorDebugEnum(_alProcesses, typeof(IDebugProcess2), typeof(IEnumDebugProcesses2));
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPort2.GetPortName(out string pbstrName)
        {
            pbstrName = _name;
            return COM_HResults.S_OK;
        }

        #endregion

        #region IDebugPortEx2 Members

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortEx2.GetProgram(IDebugProgramNode2 pProgramNode, out IDebugProgram2 ppProgram)
        {
            CorDebugAppDomain appDomain = GetAppDomain( pProgramNode );
            ppProgram = appDomain;

            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortEx2.LaunchSuspended(string pszExe, string pszArgs, string pszDir, string bstrEnv, uint hStdInput, uint hStdOutput, uint hStdError, out IDebugProcess2 ppPortProcess)
        {
            System.Windows.Forms.MessageBox.Show("Hello from LaunchSuspended!");

            ppPortProcess = null;
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortEx2.TerminateProcess(IDebugProcess2 pPortProcess)
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortEx2.ResumeProcess(IDebugProcess2 pPortProcess)
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortEx2.CanTerminateProcess(IDebugProcess2 pPortProcess)
        {
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortEx2.GetPortProcessId(out uint pdwProcessId)
        {
            pdwProcessId = 0;
            return COM_HResults.S_OK;
        }

        #endregion

        #region IDebugPortNotify2 Members

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortNotify2.AddProgramNode(IDebugProgramNode2 pProgramNode)
        {
            SendProgramEvent(pProgramNode, enum_EVENTATTRIBUTES.EVENT_SYNCHRONOUS, typeof(IDebugProgramCreateEvent2).GUID);
            return COM_HResults.S_OK;
        }

        int Microsoft.VisualStudio.Debugger.Interop.IDebugPortNotify2.RemoveProgramNode(IDebugProgramNode2 pProgramNode)
        {
            SendProgramEvent(pProgramNode, enum_EVENTATTRIBUTES.EVENT_SYNCHRONOUS, typeof(IDebugProgramDestroyEvent2).GUID);
            return COM_HResults.S_OK;
        }

        #endregion

        #region IConnectionPointContainer Members

        void Microsoft.VisualStudio.OLE.Interop.IConnectionPointContainer.FindConnectionPoint(ref Guid riid, out IConnectionPoint ppCP)
        {
            if (riid.Equals(typeof(IDebugPortEvents2).GUID))
            {
                ppCP = _cpDebugPortEvents2;
            }
            else
            {
                ppCP = null;
                Marshal.ThrowExceptionForHR(COM_HResults.CONNECT_E_NOCONNECTION);
            }
        }

        void Microsoft.VisualStudio.OLE.Interop.IConnectionPointContainer.EnumConnectionPoints(out IEnumConnectionPoints ppEnum)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
