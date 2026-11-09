//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Text;
using System.Windows.Forms.Design;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// How the virtual device is connected to Visual Studio.
    /// </summary>
    public enum VirtualDeviceTransportMode
    {
        /// <summary>A virtual serial port pair.</summary>
        Serial,

        /// <summary>TCP, no virtual serial port is needed.</summary>
        Network,
    }

    /// <summary>
    /// Tools › Options › .NET nanoFramework › Virtual Device: the virtual device (nanoCLR) running on this machine.
    /// </summary>
    /// <remarks>
    /// The virtual device is started and stopped from Device Explorer, which also follows <see cref="VirtualDeviceEnabled"/>.
    /// </remarks>
    public class NanoOptionsPageVirtualDevice : NanoOptionsPage
    {
        private const string VirtualDeviceCategory = "Virtual device";
        private const string NetworkCategory = "Virtual device network";

        private int _networkPort = NanoSettings.DefaultNetworkDevicePort;
        private int _discoveryPort = NanoSettings.DefaultNetworkDiscoveryPort;

        [Category(VirtualDeviceCategory)]
        [DisplayName("Enable virtual device")]
        [Description("Runs a virtual device (nanoCLR) on this machine, connected to a virtual serial port or through the network (see Transport). It's started with Visual Studio, and it can be started and stopped from Device Explorer. The nanoclr .NET tool is installed or updated when it's started.")]
        [DefaultValue(false)]
        public bool VirtualDeviceEnabled { get; set; }

        [Category(VirtualDeviceCategory)]
        [DisplayName("Serial port")]
        [Description("Virtual serial port of the virtual device (e.g. COM20). Leave empty to use the first virtual serial port, which is created if there's none. Used when the transport is Serial. Takes effect the next time the virtual device is started.")]
        [DefaultValue("")]
        public string VirtualDevicePort { get; set; } = string.Empty;

        [Category(VirtualDeviceCategory)]
        [DisplayName("Update nanoCLR image")]
        [Description("Updates the nanoCLR image of the virtual device when it's started with Visual Studio.")]
        [DefaultValue(true)]
        public bool VirtualDeviceAutoUpdateNanoClr { get; set; } = true;

        [Category(VirtualDeviceCategory)]
        [DisplayName("Load local nanoCLR instance")]
        [Description("Loads a local nanoCLR instance (nanoFramework.nanoCLR.dll) instead of the one installed with the nanoclr tool, from the path below. Takes effect the next time the virtual device is started.")]
        [DefaultValue(false)]
        public bool VirtualDeviceUseLocalNanoClr { get; set; }

        [Category(VirtualDeviceCategory)]
        [DisplayName("Local nanoCLR instance path")]
        [Description("Path of the local nanoCLR instance to load.")]
        [DefaultValue("")]
        [Editor(typeof(NanoClrInstanceFileNameEditor), typeof(UITypeEditor))]
        public string VirtualDeviceLocalNanoClrPath { get; set; } = string.Empty;

        [Category(NetworkCategory)]
        [DisplayName("Transport")]
        [Description("Serial connects the virtual device to a virtual serial port pair. Network makes it listen on a TCP port, a virtual serial port isn't needed. A running virtual device is restarted when this changes.")]
        [DefaultValue(VirtualDeviceTransportMode.Serial)]
        public VirtualDeviceTransportMode VirtualDeviceTransport { get; set; } = VirtualDeviceTransportMode.Serial;

        [Category(NetworkCategory)]
        [DisplayName("Listener port")]
        [Description("TCP port where the virtual device listens in network mode (1-65535). A running virtual device is restarted when this changes.")]
        [DefaultValue(NanoSettings.DefaultNetworkDevicePort)]
        public int VirtualDeviceNetworkPort
        {
            get => _networkPort;
            set => _networkPort = ValidatePort(value);
        }

        [Category(NetworkCategory)]
        [DisplayName("Discovery broadcast port")]
        [Description("UDP port where the virtual device announces itself in network mode (1-65535). Visual Studio finds the virtual device through discovery, so this port has to match the discovery port in the General options page for the virtual device to be listed. A running virtual device is restarted when this changes.")]
        [DefaultValue(NanoSettings.DefaultNetworkDiscoveryPort)]
        public int VirtualDeviceDiscoveryPort
        {
            get => _discoveryPort;
            set => _discoveryPort = ValidatePort(value);
        }

        // the values last read from NanoSettings of the settings also changed outside this page:
        // the enable by Device Explorer, the port by the virtual device when it creates one
        private bool _loadedEnabled;
        private string _loadedPort = string.Empty;

        // what the running virtual device was started with: it has to be restarted if this changes
        private bool _restartNeeded;

        protected override void LoadSettings()
        {
            VirtualDeviceEnabled = _loadedEnabled = NanoSettings.VirtualDeviceEnabled;
            VirtualDevicePort = _loadedPort = NanoSettings.VirtualDevicePort;
            VirtualDeviceAutoUpdateNanoClr = NanoSettings.VirtualDeviceAutoUpdateNanoClr;
            VirtualDeviceUseLocalNanoClr = NanoSettings.VirtualDeviceUseLocalNanoClr;
            VirtualDeviceLocalNanoClrPath = NanoSettings.VirtualDeviceLocalNanoClrPath;
            VirtualDeviceTransport = NanoSettings.VirtualDeviceUsesNetwork ? VirtualDeviceTransportMode.Network : VirtualDeviceTransportMode.Serial;
            VirtualDeviceNetworkPort = NanoSettings.VirtualDeviceNetworkPort;
            VirtualDeviceDiscoveryPort = NanoSettings.VirtualDeviceDiscoveryPort;
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            base.OnActivate(e);

            // the page is kept between openings of the Options dialog: show the changes made outside it, unless they were edited here
            if (VirtualDeviceEnabled == _loadedEnabled)
            {
                VirtualDeviceEnabled = NanoSettings.VirtualDeviceEnabled;
            }

            if (PortEquals(VirtualDevicePort, _loadedPort))
            {
                VirtualDevicePort = NanoSettings.VirtualDevicePort;
            }

            _loadedEnabled = NanoSettings.VirtualDeviceEnabled;
            _loadedPort = NanoSettings.VirtualDevicePort;
        }

        protected override void SaveSettings()
        {
            // only what was edited here: an unchanged value mustn't overwrite a change made outside the page
            if (!PortEquals(VirtualDevicePort, _loadedPort))
            {
                NanoSettings.VirtualDevicePort = _loadedPort = VirtualDevicePort?.Trim();
            }

            NanoSettings.VirtualDeviceAutoUpdateNanoClr = VirtualDeviceAutoUpdateNanoClr;
            NanoSettings.VirtualDeviceUseLocalNanoClr = VirtualDeviceUseLocalNanoClr;
            NanoSettings.VirtualDeviceLocalNanoClrPath = VirtualDeviceLocalNanoClrPath?.Trim();

            SaveNetworkSettings();

            // the other settings first: changing the enable starts or stops the virtual device
            if (VirtualDeviceEnabled != _loadedEnabled)
            {
                NanoSettings.VirtualDeviceEnabled = _loadedEnabled = VirtualDeviceEnabled;
            }
        }

        private void SaveNetworkSettings()
        {
            string transport = VirtualDeviceTransport == VirtualDeviceTransportMode.Network
                ? NanoSettings.VirtualDeviceTransportNetwork
                : NanoSettings.VirtualDeviceTransportSerial;

            _restartNeeded = !string.Equals(NanoSettings.VirtualDeviceTransport, transport, StringComparison.OrdinalIgnoreCase)
                || NanoSettings.VirtualDeviceNetworkPort != VirtualDeviceNetworkPort
                || NanoSettings.VirtualDeviceDiscoveryPort != VirtualDeviceDiscoveryPort;

            NanoSettings.VirtualDeviceTransport = transport;
            NanoSettings.VirtualDeviceNetworkPort = VirtualDeviceNetworkPort;
            NanoSettings.VirtualDeviceDiscoveryPort = VirtualDeviceDiscoveryPort;
        }

        protected override void OnSettingsSaved()
        {
            if (!_restartNeeded)
            {
                return;
            }

            _restartNeeded = false;

            if (NanoSettings.VirtualDeviceEnabled
                && DeviceOperations.IsVirtualDeviceRunning)
            {
                // the new transport and ports are used by the next instance
                _ = DeviceOperations.RestartVirtualDeviceAsync();
            }
        }

        private static int ValidatePort(int port)
        {
            if (!NetworkDeviceAddress.IsValidPort(port))
            {
                throw new ArgumentOutOfRangeException(nameof(port), "The port has to be between 1 and 65535.");
            }

            return port;
        }

        private static bool PortEquals(string port, string other) => string.Equals(port?.Trim() ?? string.Empty, other ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// File picker for the nanoCLR instance, a DLL.
        /// </summary>
        private sealed class NanoClrInstanceFileNameEditor : FileNameEditor
        {
            protected override void InitializeDialog(OpenFileDialog openFileDialog)
            {
                base.InitializeDialog(openFileDialog);

                openFileDialog.Title = "Select nanoCLR instance";
                openFileDialog.Filter = "nanoCLR instance (*.dll)|*.dll";
            }
        }
    }
}
