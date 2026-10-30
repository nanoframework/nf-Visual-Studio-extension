//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Tools › Options › .NET nanoFramework › General: deployment and device connection.
    /// </summary>
    public class NanoOptionsPageGeneral : NanoOptionsPage
    {
        private const string DeploymentCategory = "Deployment";
        private const string DevicesCategory = "Devices";

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

        protected override void LoadSettings()
        {
            GenerateDeploymentImage = NanoSettings.GenerateDeploymentImage;
            IncludeConfigBlockInDeploymentImage = NanoSettings.IncludeConfigBlockInDeploymentImage;
            FlashDumpCachePath = NanoSettings.FlashDumpCachePath;
            PortExclusionList = NanoSettings.PortExclusionList;
        }

        protected override void SaveSettings()
        {
            NanoSettings.GenerateDeploymentImage = GenerateDeploymentImage;
            NanoSettings.IncludeConfigBlockInDeploymentImage = IncludeConfigBlockInDeploymentImage;
            NanoSettings.FlashDumpCachePath = FlashDumpCachePath?.Trim();
            NanoSettings.PortExclusionList = PortExclusionList?.Trim();
        }

        protected override void OnSettingsSaved()
        {
            // the device watchers use the new list from now on
            NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortExclusionList = NanoSettings.GetPortExclusionList();
        }
    }
}
