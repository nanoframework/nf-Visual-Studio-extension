//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using Microsoft.VisualStudio.Imaging.Interop;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public static class NanoFrameworkMonikers
    {
        // GUID matching the NanoFrameworkCatalog
        private static readonly Guid ManifestGuid = new Guid("6CBBCC35-4BB4-4110-AFFF-9BAEF4DAA4A7");

        public static ImageMoniker NanoFramework
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 40 };
            }
        }

        public static ImageMoniker DeviceConnected
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 20 };
            }
        }

        public static ImageMoniker DeviceDisconnected
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 30 };
            }
        }

        public static ImageMoniker Ping
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 50 };
            }
        }

        public static ImageMoniker DeviceCapabilities
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 70 };
            }
        }

        public static ImageMoniker NanoFrameworkProject
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 80 };
            }
        }

        public static ImageMoniker ShowInternalErrors
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 90 };
            }
        }

        public static ImageMoniker DeviceErase
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 100 };
            }
        }

        public static ImageMoniker NetworkConfig
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 110 };
            }
        }

        public static ImageMoniker Reboot
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 120 };
            }
        }

        public static ImageMoniker DisableDeviceWatchers
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 130 };
            }
        }

        public static ImageMoniker RescanDevices
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 140 };
            }
        }

        public static ImageMoniker SettingsID
        {
            get
            {
                return new ImageMoniker { Guid = ManifestGuid, Id = 150 };
            }
        }
    }
}
