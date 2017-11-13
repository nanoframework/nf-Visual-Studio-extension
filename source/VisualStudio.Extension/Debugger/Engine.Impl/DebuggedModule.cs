using System.Collections.Generic;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class DebuggedModule
    {
        private uint _loadOrder;

        public string Id { get; private set; }
        public string Name { get; private set; }

        public readonly uint BaseAddress;
        public readonly uint Size;
        public bool SymbolsLoaded { get; private set; }
        public string SymbolPath { get; private set; }

        public uint GetLoadOrder() { return _loadOrder; }

        public bool AddressInModule(ulong address)
        {
            return true;
        }
    }
}