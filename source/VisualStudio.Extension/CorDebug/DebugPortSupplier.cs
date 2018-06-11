//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Debugger.Interop;
using nanoFramework.Tools.Debugger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    // This Guid needs to match PortSupplierCLSID
    [ComVisible(true), Guid("78E48437-246C-4CA2-B66F-4B65AEED8500")]
    public class DebugPortSupplier : IDebugPortSupplier2, IDebugPortSupplierEx2, IDebugPortSupplierDescription2
    {
        public const string PortSupplierId = "D7240956-FE4A-4324-93C9-C56975AF351E";

        // This Guid needs to match PortSupplierGuid
        public static Guid PortSupplierGuid => new Guid(PortSupplierId);

        private static DebugPortSupplierPrivate s_portSupplier;

        private class DebugPortSupplierPrivate : DebugPortSupplier
        {                        
            private IDebugCoreServer2 _server;
            List<DebugPort> _ports = new List<DebugPort>();

            public DebugPortSupplierPrivate() : base(true)
            {
            }

            public override DebugPort FindPort(string name)
            {
                return _ports.FirstOrDefault(p => p.Name == name);
            }

            public override IDebugCoreServer2 CoreServer
            {
                [System.Diagnostics.DebuggerHidden]
                get { return _server; }
            }

            public override DebugPort[] Ports
            {
                get { return (DebugPort[])_ports.ToArray().Clone(); }
            }

            #region IDebugPortSupplier2 Members

            new public int GetPortSupplierId(out Guid pguidPortSupplier)
            {
                pguidPortSupplier = DebugPortSupplier.PortSupplierGuid;
                return COM_HResults.S_OK;
            }

            new public int EnumPorts(out IEnumDebugPorts2 ppEnum)
            {
                List<IDebugPort2> ports = new List<IDebugPort2>();
                _ports.Clear();

                foreach (NanoDeviceBase device in NanoFrameworkPackage.NanoDeviceCommService.DebugClient.NanoFrameworkDevices)
                {
                    ports.Add(new DebugPort(device, this));
                    _ports.Add(new DebugPort(device, this));
                }

                ppEnum = new CorDebugEnum(ports, typeof(IDebugPort2), typeof(IEnumDebugPorts2));
                return COM_HResults.S_OK;
            }

            new public int AddPort(IDebugPortRequest2 pRequest, out IDebugPort2 ppPort)
            {
                IEnumDebugPorts2 portList;
                EnumPorts(out portList);

                string name;
                pRequest.GetPortName(out name);
                ppPort = FindPort(name);

                if (ppPort != null)
                {
                    return COM_HResults.S_OK;
                }

                return COM_HResults.E_FAIL;
            }

            new public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort)
            {
                Guid guidPortToFind = guidPort;

                ppPort = _ports.FirstOrDefault(p => p.PortId == guidPortToFind);

                if (ppPort != null)
                {
                    return COM_HResults.S_OK;
                }

                return COM_HResults.E_FAIL;
            }

            #endregion

            #region IDebugPortSupplierEx2 Members

            new public int SetServer(IDebugCoreServer2 pServer)
            {
                _server = pServer;
                return COM_HResults.S_OK;
            }

            #endregion
        }

        private DebugPortSupplier( bool fPrivate )
        {
        }

        public DebugPortSupplier()
        {
            lock (typeof(DebugPortSupplier))
            {
                if(s_portSupplier == null)
                {
                    s_portSupplier = new DebugPortSupplierPrivate();
                }
            }
        }
        public virtual DebugPort[] Ports
        {
            get { return s_portSupplier.Ports; }
        }

        public virtual DebugPort FindPort(string name)
        {
            return s_portSupplier.FindPort(name);
        }

        public virtual IDebugCoreServer2 CoreServer
        {
            get { return s_portSupplier.CoreServer; }
        }

        #region IDebugPortSupplierEx2 Members

        public int SetServer(IDebugCoreServer2 pServer) 
        {
            return s_portSupplier.SetServer(pServer);
        }

        #endregion
        
        #region IDebugPortSupplier2 Members

        public int GetPortSupplierId(out Guid pguidPortSupplier) 
        {
            return s_portSupplier.GetPortSupplierId(out pguidPortSupplier);
        }

        public int RemovePort(IDebugPort2 pPort)
        {
            return COM_HResults.E_NOTIMPL;
        }

        public int CanAddPort()
        {
            return COM_HResults.S_OK;
        }

        public int GetPortSupplierName(out string name)
        {
            name = ".NET nanoFramework Device";
            return COM_HResults.S_OK;
        }

        public int EnumPorts(out IEnumDebugPorts2 ppEnum)
        {
            return s_portSupplier.EnumPorts(out ppEnum);
        }

        public int AddPort(IDebugPortRequest2 pRequest, out IDebugPort2 ppPort)
        {
            return s_portSupplier.AddPort(pRequest, out ppPort);
        }

        public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort)
        {
            return s_portSupplier.GetPort(ref guidPort, out ppPort);
        }

        #endregion

        #region IDebugPortSupplierDescription2 Members

        int IDebugPortSupplierDescription2.GetDescription(enum_PORT_SUPPLIER_DESCRIPTION_FLAGS[] pdwFlags, out string pbstrText)
        {
            pbstrText = "Use this transport to connect to all nanoFramework devices.";

            return COM_HResults.S_OK;
        }

        #endregion
    }
}
