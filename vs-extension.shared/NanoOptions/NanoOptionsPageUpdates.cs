//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Tools › Options › .NET nanoFramework › Updates: firmware updates of the connected devices.
    /// </summary>
    public class NanoOptionsPageUpdates : NanoOptionsPage
    {
        private const string FirmwareCategory = "Firmware";

        [Category(FirmwareCategory)]
        // the grid sorts by name: this one comes before "Include preview firmware"
        [DisplayName("Automatically update devices")]
        [Description("Checks for a newer firmware when a device is connected, and updates it.")]
        [DefaultValue(false)]
        public bool AutoUpdateEnabled { get; set; }

        [Category(FirmwareCategory)]
        [DisplayName("Include preview firmware")]
        [Description("Includes the preview versions of the firmware in the updates.")]
        [DefaultValue(true)]
        public bool IncludePrereleaseUpdates { get; set; } = true;

        protected override void LoadSettings()
        {
            AutoUpdateEnabled = NanoSettings.AutoUpdateEnabled;
            IncludePrereleaseUpdates = NanoSettings.IncludePrereleaseUpdates;
        }

        protected override void SaveSettings()
        {
            NanoSettings.AutoUpdateEnabled = AutoUpdateEnabled;
            NanoSettings.IncludePrereleaseUpdates = IncludePrereleaseUpdates;
        }
    }
}
