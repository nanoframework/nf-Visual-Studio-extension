using System;
using Microsoft.VisualStudio.Debugger.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class NanoDebugPort : IDebugPort2
    {
        private NanoDebugPortSupplier _nanoDebugPortSupplier;
        private IDebugPortRequest2 _pRequest;

        public NanoDebugPort(NanoDebugPortSupplier nanoDebugPortSupplier, IDebugPortRequest2 pRequest)
        {
            _nanoDebugPortSupplier = nanoDebugPortSupplier;
            _pRequest = pRequest;
        }


        #region IDebugPort2 Members

        public int GetPortName(out string pbstrName)
        {
            throw new NotImplementedException();
        }

        public int GetPortId(out Guid pguidPort)
        {
            throw new NotImplementedException();
        }

        public int GetPortRequest(out IDebugPortRequest2 ppRequest)
        {
            throw new NotImplementedException();
        }

        public int GetPortSupplier(out IDebugPortSupplier2 ppSupplier)
        {
            throw new NotImplementedException();
        }

        public int GetProcess(AD_PROCESS_ID ProcessId, out IDebugProcess2 ppProcess)
        {
            throw new NotImplementedException();
        }

        public int EnumProcesses(out IEnumDebugProcesses2 ppEnum)
        {
            throw new NotImplementedException();
        }

        #endregion

    }
}