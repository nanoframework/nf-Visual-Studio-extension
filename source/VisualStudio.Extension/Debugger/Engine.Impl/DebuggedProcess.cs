using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class DebuggedProcess
    {
        public AD_PROCESS_ID Id { get; private set; }
        public AD7Engine Engine { get; private set; }

        private List<DebuggedModule> _moduleList;

        public uint[] GetAddressesForSourceLocation(string moduleName, string documentName, uint dwStartLine, uint dwStartCol)
        {
            uint[] addrs = new uint[1];
            addrs[0] = 0xDEADF00D;
            return addrs;
        }

        public void SetBreakpoint(uint address, Object client)
        {
            throw new NotImplementedException();
        }

        public DebuggedModule ResolveAddress(ulong addr)
        {
            lock (_moduleList)
            {
                return _moduleList.Find((m) => m.AddressInModule(addr));
            }
        }
    }
}