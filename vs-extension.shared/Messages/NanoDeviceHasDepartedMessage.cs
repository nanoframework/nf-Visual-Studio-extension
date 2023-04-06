using System;
using System.Collections.Generic;
using System.Text;

namespace vs_extension.shared.Messages
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
