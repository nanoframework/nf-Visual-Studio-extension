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

        protected override void LoadSettings()
        {
            VirtualDeviceEnabled = NanoSettings.VirtualDeviceEnabled;
            VirtualDevicePort = NanoSettings.VirtualDevicePort;
            VirtualDeviceAutoUpdateNanoClr = NanoSettings.VirtualDeviceAutoUpdateNanoClr;
            VirtualDeviceUseLocalNanoClr = NanoSettings.VirtualDeviceUseLocalNanoClr;
            VirtualDeviceLocalNanoClrPath = NanoSettings.VirtualDeviceLocalNanoClrPath;
        }

        protected override void SaveSettings()
        {
            // the other settings first: changing the enable starts or stops the virtual device
            NanoSettings.VirtualDevicePort = VirtualDevicePort?.Trim();
            NanoSettings.VirtualDeviceAutoUpdateNanoClr = VirtualDeviceAutoUpdateNanoClr;
            NanoSettings.VirtualDeviceUseLocalNanoClr = VirtualDeviceUseLocalNanoClr;
            NanoSettings.VirtualDeviceLocalNanoClrPath = VirtualDeviceLocalNanoClrPath?.Trim();
            NanoSettings.VirtualDeviceEnabled = VirtualDeviceEnabled;
        }

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
