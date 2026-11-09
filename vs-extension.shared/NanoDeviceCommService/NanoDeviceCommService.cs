// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.PortTcpIp;

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
                    List<string> serialPortList = NanoSettings.GetPortExclusionList();

                    // create serial instance with device watchers stopped
                    PortBase serialDebug = PortBase.CreateInstanceForSerial(false, serialPortList);

                    // create network instance with device watchers stopped
                    // the network devices are the ones that announce themselves, with discovery off the UDP listener is never opened and none is listed
                    _networkManager = new PortTcpIpManager(false, NanoSettings.NetworkDiscoveryPort, NanoSettings.NetworkDiscoveryEnabled);

                    // the tokens are kept by the extension
                    _networkManager.TokenProvider = NetworkDeviceTokens.GetDeviceToken;

                    var ports = new List<PortBase> { serialDebug, _networkManager };

                    // create composite client with all ports
                    // OK to start device watchers, if setting is enabled
                    _debugClient = PortBase.CreateInstanceForComposite(
                        ports,
                        !NanoSettings.DeviceWatchersDisabled);

                    if (!NanoSettings.DeviceWatchersDisabled)
                    {
                        // output progress message
                        MessageCentre.StartProgressMessage(MessageCentre.MessageStartSearchingDevices);
                    }
                }

                return _debugClient;
            }
        }

        PortTcpIpManager _networkManager = null;

        /// <summary>
        /// The manager of the network devices.
        /// </summary>
        public PortTcpIpManager NetworkManager
        {
            get
            {
                // created with the debug client
                _ = DebugClient;

                return _networkManager;
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
    }
}
