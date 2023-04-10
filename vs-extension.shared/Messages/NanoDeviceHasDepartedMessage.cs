using System;
using System.Collections.Generic;
using System.Text;
﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension.Messages
{
    public sealed class NanoDeviceHasDepartedMessage
    {
        public string DeviceId { get; }

        public NanoDeviceHasDepartedMessage(string deviceId)
        {
            DeviceId = deviceId;
        }
    }
}
