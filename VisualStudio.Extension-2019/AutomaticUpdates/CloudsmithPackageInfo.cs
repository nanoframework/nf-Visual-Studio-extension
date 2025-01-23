﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Newtonsoft.Json;
using System;

namespace nanoFramework.Tools.VisualStudio.Extension.FirmwareUpdate
{
    [Serializable]
    internal class CloudsmithPackageInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("cdn_url")]
        public string DownloadUrl { get; set; }
    }
}
