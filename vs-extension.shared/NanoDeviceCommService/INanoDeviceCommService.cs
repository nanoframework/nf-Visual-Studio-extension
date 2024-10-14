// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public interface INanoDeviceCommService
    {
        PortBase DebugClient { get; }

        NanoDeviceBase Device { get; }

        bool SelectDevice(string description);

        TaskAwaiter GetAwaiter();
    }
}
