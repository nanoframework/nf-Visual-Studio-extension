using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [ComVisible(true)]
    [Guid(DebuggerGuids.NanoDebugPortSupplierCLSIDAsString)]
    public class NanoDebugPortSupplier : IDebugPortSupplier2, IDebugPortSupplierDescription2
    {
        private const string Name = "nanoCLR";

        public const string PortSupplierId = "{69FA8E4E-D542-415A-9756-CA4A00932B9E}";
        public static readonly Guid PortSupplierGuid = new Guid(PortSupplierId);
        private readonly List<IDebugPort2> _ports = new List<IDebugPort2>();


        public NanoDebugPortSupplier()
        {

        }

        // Qualifier for our transport has one of the following formats:

        public int AddPort(IDebugPortRequest2 pRequest, out IDebugPort2 ppPort)
        {
            ppPort = null;

            string name;
            pRequest.GetPortName(out name);

            var port = new NanoDebugPort(this, pRequest);

            if (ppPort == null)
            {
                return VSConstants.E_FAIL;
            }

            ppPort = port;
            _ports.Add(port);

            return VSConstants.S_OK;
        }

        public int CanAddPort()
        {
            return VSConstants.S_OK; // S_OK = true, S_FALSE = false
        }

        public int EnumPorts(out IEnumDebugPorts2 ppEnum)
        {
            ppEnum = new AD7DebugPortsEnum(_ports.ToArray());
            return VSConstants.S_OK;
        }

        public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort)
        {
            // Never called, so this code has not been verified
            foreach (var port in _ports)
            {
                Guid currentGuid;
                if (port.GetPortId(out currentGuid) == VSConstants.S_OK && currentGuid == guidPort)
                {
                    ppPort = port;
                    return VSConstants.S_OK;
                }
            }
            ppPort = null;
            return AD7_HRESULT.E_PORTSUPPLIER_NO_PORT;
        }

        public int GetPortSupplierId(out Guid pguidPortSupplier)
        {
            pguidPortSupplier = PortSupplierGuid;
            return VSConstants.S_OK;
        }

        public int GetPortSupplierName(out string pbstrName)
        {
            pbstrName = "Python remote (ptvsd)";
            return VSConstants.S_OK;
        }

        public int RemovePort(IDebugPort2 pPort)
        {
            // Never called, so this code has not been verified
            bool removed = _ports.Remove(pPort);
            return removed ? VSConstants.S_OK : AD7_HRESULT.E_PORTSUPPLIER_NO_PORT;
        }

        public int GetDescription(enum_PORT_SUPPLIER_DESCRIPTION_FLAGS[] pdwFlags, out string pbstrText)
        {
            pbstrText = "description for nanoFramework port supplier";
            return VSConstants.S_OK;
        }
    }
}
