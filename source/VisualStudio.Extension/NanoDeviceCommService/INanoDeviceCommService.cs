//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public interface INanoDeviceCommService
    {
        PortBase DebugClient { get; }

        NanoDeviceBase Device { get; }

        bool SelectDevice(string description);

        Task CreateDebugClientsAsync();

        TaskAwaiter GetAwaiter();

        Task<bool> ConnectToAsync(string description = null, int timeout = 5000);
    }
}
