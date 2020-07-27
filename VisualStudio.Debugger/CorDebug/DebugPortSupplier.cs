using Microsoft.VisualStudio.Debugger.Interop;
using nanoFramework.Tools.Debugger;
using System;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Debugger
{
    // This Guid needs to match PortSupplierCLSID
    [ComVisible(true), Guid("78E48437-246C-4CA2-B66F-4B65AEED8500")]
    public class DebugPortSupplier : IDebugPortSupplier2, IDebugPortSupplierEx2, IDebugPortSupplierDescription2
    {
        // This Guid needs to match PortSupplierGuid
        public static Guid PortSupplierGuid = new Guid("D7240956-FE4A-4324-93C9-C56975AF351E");
        private static DebugPortSupplierPrivate s_portSupplier;        

        private class DebugPortSupplierPrivate : DebugPortSupplier
        {                        
            private DebugPort[] _ports;
            private IDebugCoreServer2 _server;

            public DebugPortSupplierPrivate() : base(true)
            {
                _ports = new DebugPort[] {
                                            //new DebugPort(PortFilter.Emulator, this),
                                            new DebugPort(PortFilter.Usb, this),
                                            //new DebugPort(PortFilter.Serial, this),
                                            //new DebugPort(PortFilter.TcpIp, this),
                                            };

            }
            
            public override DebugPort FindPort(string name)
            {
                for(int i = 0; i < _ports.Length; i++)
                {
                    DebugPort port = _ports[i];

                    if (String.Compare(port.Name, name, true) == 0)
                    {
                        return port;
                    }
                }

                return null;
            }

            public override DebugPort[] Ports
            {
                get {return (DebugPort[])_ports.Clone();}
            }

            public override IDebugCoreServer2 CoreServer
            {
                [System.Diagnostics.DebuggerHidden]
                get { return _server; }
            }

            private DebugPort DebugPortFromPortFilter(PortFilter portFilter)
            {
                foreach (DebugPort port in _ports)
                {
                    if (port.PortFilter == portFilter)
                        return port;
                }

                return null;
            }

            #region IDebugPortSupplier2 Members

            new public int GetPortSupplierId(out Guid pguidPortSupplier)
            {
                pguidPortSupplier = DebugPortSupplier.PortSupplierGuid;
                return COM_HResults.S_OK;
            }

            new public int EnumPorts(out IEnumDebugPorts2 ppEnum)
            {
                ppEnum = new CorDebugEnum(_ports, typeof(IDebugPort2), typeof(IEnumDebugPorts2));
                return COM_HResults.S_OK;
            }

            new public int AddPort(IDebugPortRequest2 pRequest, out IDebugPort2 ppPort)
            {
                string name;

                pRequest.GetPortName(out name);
                ppPort = FindPort(name);

                if (ppPort == null)
                {
                    DebugPort port = _ports[(int)PortFilter.TcpIp];
                    //hack, hack.  Abusing the Attach to dialog box to force the NetworkDevice port to 
                    //look for a nanoCLR process
                    if (port.TryAddProcess(name) != null)
                    {
                        ppPort = port;
                    }
                }

                return COM_HResults.BOOL_TO_HRESULT_FAIL( ppPort != null );                
            }

            new public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort)
            {
                ppPort = null;
                foreach (DebugPort port in _ports)
                {
                    if (guidPort.Equals(port.PortId))
                    {
                        ppPort = port;
                        break;
                    }
                }
                return COM_HResults.S_OK;
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
            return COM_HResults.S_FALSE;
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
