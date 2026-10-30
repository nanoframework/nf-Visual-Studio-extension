//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Tools › Options › .NET nanoFramework › Virtual Device: the virtual device (nanoCLR) running on this machine.
    /// </summary>
    /// <remarks>
    /// The virtual device is started and stopped from Device Explorer, which also follows <see cref="VirtualDeviceEnabled"/>.
    /// </remarks>
    public class NanoOptionsPageVirtualDevice : NanoOptionsPage
    {
        private const string VirtualDeviceCategory = "Virtual device";

        [Category(VirtualDeviceCategory)]
        [DisplayName("Enable virtual device")]
        [Description("Runs a virtual device (nanoCLR) on this machine, connected to a virtual serial port. It's started with Visual Studio, and it can be started and stopped from Device Explorer. The nanoclr .NET tool is installed or updated when it's started.")]
        [DefaultValue(false)]
        public bool VirtualDeviceEnabled { get; set; }

        [Category(VirtualDeviceCategory)]
        [DisplayName("Serial port")]
        [Description("Virtual serial port of the virtual device (e.g. COM20). Leave empty to use the first virtual serial port, which is created if there's none. Takes effect the next time the virtual device is started.")]
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

        // the values last read from NanoSettings of the settings also changed outside this page:
        // the enable by Device Explorer, the port by the virtual device when it creates one
        private bool _loadedEnabled;
        private string _loadedPort = string.Empty;

        protected override void LoadSettings()
        {
            VirtualDeviceEnabled = _loadedEnabled = NanoSettings.VirtualDeviceEnabled;
            VirtualDevicePort = _loadedPort = NanoSettings.VirtualDevicePort;
            VirtualDeviceAutoUpdateNanoClr = NanoSettings.VirtualDeviceAutoUpdateNanoClr;
            VirtualDeviceUseLocalNanoClr = NanoSettings.VirtualDeviceUseLocalNanoClr;
            VirtualDeviceLocalNanoClrPath = NanoSettings.VirtualDeviceLocalNanoClrPath;
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

            // the other settings first: changing the enable starts or stops the virtual device
            if (VirtualDeviceEnabled != _loadedEnabled)
            {
                NanoSettings.VirtualDeviceEnabled = _loadedEnabled = VirtualDeviceEnabled;
            }
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
