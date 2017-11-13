using System;
using Microsoft.VisualStudio.Debugger.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    // This class manages breakpoints for the engine. 
    internal class BreakpointManager
    {
        private AD7Engine _engine;
        private System.Collections.Generic.List<AD7PendingBreakpoint> _pendingBreakpoints;

        public BreakpointManager(AD7Engine engine)
        {
            _engine = engine;
            _pendingBreakpoints = new System.Collections.Generic.List<AD7PendingBreakpoint>();
        }
      
        // A helper method used to construct a new pending breakpoint.
        public void CreatePendingBreakpoint(IDebugBreakpointRequest2 pBPRequest, out IDebugPendingBreakpoint2 ppPendingBP)
        {
            AD7PendingBreakpoint pendingBreakpoint = new AD7PendingBreakpoint(pBPRequest, _engine, this);
            ppPendingBP = (IDebugPendingBreakpoint2)pendingBreakpoint;
            lock (_pendingBreakpoints)
            {
                _pendingBreakpoints.Add(pendingBreakpoint);
            }
        }

        // Called from the engine's detach method to remove the debugger's breakpoint instructions.
        public void ClearBoundBreakpoints()
        {
            lock (_pendingBreakpoints)
            {
                foreach (AD7PendingBreakpoint pendingBreakpoint in _pendingBreakpoints)
                {
                    pendingBreakpoint.ClearBoundBreakpoints();
                }
            }
        }
    }
}
