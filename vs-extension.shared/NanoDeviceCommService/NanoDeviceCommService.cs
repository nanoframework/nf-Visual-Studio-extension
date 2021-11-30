//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class NanoDeviceCommService : SNanoDeviceCommService, INanoDeviceCommService
    {
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider _serviceProvider;

        public NanoDeviceBase Device { get; internal set; } = null;

        PortBase _debugClient = null;

        public PortBase DebugClient
        {
            get
            {
                if (_debugClient == null)
                {
                    // grab and parse COM port list
                    List<string> serialPortList = new List<string>();
                    
                    // need to wrap the processing in a try/catch to deal with bad user input/format
                    try
                    {
                        // grab and parse COM port list
                        if (!string.IsNullOrEmpty(NanoFrameworkPackage.SettingPortBlackList))
                        {
                            serialPortList.AddRange(NanoFrameworkPackage.SettingPortBlackList.Split(';'));
                        }
                    }
                    catch
                    {
                        // don't care about bad user input/format/etc 
                    }

                    // create serial instance with device watchers stopped
                    PortBase serialDebug = PortBase.CreateInstanceForSerial(false, serialPortList);

                    // create network instance with device watchers stopped
                    PortBase networkDebug = PortBase.CreateInstanceForNetwork(false);

                    // create composite client with all ports
                    // start device watcher (or not) according to current user option
                    _debugClient = PortBase.CreateInstanceForComposite(
                        new[] { serialDebug, networkDebug },
                        !NanoFrameworkPackage.OptionDisableDeviceWatchers);
                }

                return _debugClient;
            }
        }

        public NanoDeviceCommService(Microsoft.VisualStudio.Shell.IAsyncServiceProvider provider)
        {
            _serviceProvider = provider;
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

            return true;
        }

        public bool ConnectTo(string deviceId = null, int timeout = 5000)
        {
            if (deviceId == null)
            {
                return Device.DebugEngine.Connect(timeout, true);
            }
            else
            {
                return DebugClient.NanoFrameworkDevices.FirstOrDefault(d => d.Description == deviceId).DebugEngine.Connect(timeout, true);
            }
        }
    }
}
