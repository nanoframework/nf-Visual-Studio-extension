//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Tools › Options › .NET nanoFramework › General: deployment and device connection.
    /// </summary>
    public class NanoOptionsPageGeneral : NanoOptionsPage
    {
        private const string DeploymentCategory = "Deployment";
        private const string DevicesCategory = "Devices";
        private const string NetworkDevicesCategory = "Network devices";

        private int _networkDiscoveryPort = NanoSettings.DefaultNetworkDiscoveryPort;
        private bool _restartNeeded;

        [Category(DeploymentCategory)]
        [DisplayName("Generate deployment image")]
        [Description("Generates a deployment image on deploy: a binary file with the assemblies of the application, ready to be flashed to a device. The image is placed in the project output folder.")]
        [DefaultValue(false)]
        public bool GenerateDeploymentImage { get; set; }

        [Category(DeploymentCategory)]
        [DisplayName("Include configuration block")]
        [Description("Includes the configuration block of the device (network configuration, certificates and other settings) in the deployment image.")]
        [DefaultValue(false)]
        public bool IncludeConfigBlockInDeploymentImage { get; set; }

        [Category(DeploymentCategory)]
        [DisplayName("Flash dump cache folder")]
        [Description("Folder where the flash dumps of the devices are cached, to generate deployment images. Leave empty to cache them in the project output folder.")]
        [DefaultValue("")]
        [Editor(typeof(FolderNameEditor), typeof(UITypeEditor))]
        public string FlashDumpCachePath { get; set; } = string.Empty;

        [Category(DevicesCategory)]
        [DisplayName("Excluded serial ports")]
        [Description("Serial ports where no device is searched for, separated by semicolons (e.g. COM1;COM5). Use it for ports that are used by other devices or applications.")]
        [DefaultValue("")]
        public string PortExclusionList { get; set; } = string.Empty;

        [Category(NetworkDevicesCategory)]
        [DisplayName("Find network devices")]
        [Description("Finds the devices that announce themselves on the network (UDP broadcast), to debug them over TCP. Network devices are only listed when they are found this way: with this off, no network devices are listed at all, including the virtual device in network mode. Takes effect the next time Visual Studio starts.")]
        [DefaultValue(true)]
        public bool NetworkDiscoveryEnabled { get; set; } = true;

        [Category(NetworkDevicesCategory)]
        [DisplayName("Discovery port")]
        [Description("UDP port where the network devices announce themselves (1-65535). Takes effect the next time Visual Studio starts.")]
        [DefaultValue(NanoSettings.DefaultNetworkDiscoveryPort)]
        public int NetworkDiscoveryPort
        {
            get => _networkDiscoveryPort;
            set
            {
                if (!NetworkDeviceAddress.IsValidPort(value))
                {
                    throw new System.ArgumentOutOfRangeException(nameof(value), "The port has to be between 1 and 65535.");
                }

                _networkDiscoveryPort = value;
            }
        }

        protected override void LoadSettings()
        {
            GenerateDeploymentImage = NanoSettings.GenerateDeploymentImage;
            IncludeConfigBlockInDeploymentImage = NanoSettings.IncludeConfigBlockInDeploymentImage;
            FlashDumpCachePath = NanoSettings.FlashDumpCachePath;
            PortExclusionList = NanoSettings.PortExclusionList;
            NetworkDiscoveryEnabled = NanoSettings.NetworkDiscoveryEnabled;
            NetworkDiscoveryPort = NanoSettings.NetworkDiscoveryPort;
        }

        protected override void SaveSettings()
        {
            // the network device watchers are created when Visual Studio starts, so these need a restart to take effect
            _restartNeeded = NanoSettings.NetworkDiscoveryEnabled != NetworkDiscoveryEnabled
                || NanoSettings.NetworkDiscoveryPort != NetworkDiscoveryPort;

            NanoSettings.GenerateDeploymentImage = GenerateDeploymentImage;
            NanoSettings.IncludeConfigBlockInDeploymentImage = IncludeConfigBlockInDeploymentImage;
            NanoSettings.FlashDumpCachePath = FlashDumpCachePath?.Trim();
            NanoSettings.PortExclusionList = PortExclusionList?.Trim();
            NanoSettings.NetworkDiscoveryEnabled = NetworkDiscoveryEnabled;
            NanoSettings.NetworkDiscoveryPort = NetworkDiscoveryPort;
        }

        protected override void OnSettingsSaved()
        {
            // the device watchers use the new list from now on
            NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortExclusionList = NanoSettings.GetPortExclusionList();

            if (_restartNeeded)
            {
                _restartNeeded = false;

                ThreadHelper.ThrowIfNotOnUIThread();

                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    "The network device settings you changed take effect after restarting Visual Studio.",
                    ".NET nanoFramework",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
