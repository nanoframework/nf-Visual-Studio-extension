//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class NanoDeviceCommService : SNanoDeviceCommService, INanoDeviceCommService
    {
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider _serviceProvider;

        public NanoDeviceBase Device { get; internal set; } = null;

        public PortBase DebugClient { get; internal set; }

        public NanoDeviceCommService(Microsoft.VisualStudio.Shell.IAsyncServiceProvider provider)
        {
            _serviceProvider = provider;

            // launches the serial client and service
            // create serial instance WITHOUT app associated because we don't care of app life cycle in VS extension
            DebugClient = PortBase.CreateInstanceForSerial("", null);
        }

        public TaskAwaiter GetAwaiter()
        {
            return new TaskAwaiter();
        }

        public bool SelectDevice(string deviceId = null)
        {
            NanoDeviceBase device = null;

            if (deviceId != null)
            {
                // check if this device is available
                device = DebugClient.NanoFrameworkDevices.FirstOrDefault(d => d.Description == deviceId);
            }

            Device = device;

            // has we'll be needing the debugger engine anyway, create it if needed
            if (device != null)
            {
                if (device.DebugEngine == null)
                {
                    device.CreateDebugEngine();
                }
            }

            return true;
        }

        public async Task<bool> ConnectToAsync(string deviceId = null, int timeout = 5000)
        {
            if (deviceId == null)
            {
                return await Device.DebugEngine.ConnectAsync(timeout, true);
            }
            else
            {
                return await DebugClient.NanoFrameworkDevices.FirstOrDefault(d => d.Description == deviceId).DebugEngine.ConnectAsync(timeout, true);
            }
        }
    }
}
