//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow
{
    public class NFSerialDebugClientService : INFSerialDebugClientService
    {
        public PortBase SerialDebugClient { get; private set; }

        public NFSerialDebugClientService(PortBase client)
        {
            SerialDebugClient = client;
        }

    }
}
