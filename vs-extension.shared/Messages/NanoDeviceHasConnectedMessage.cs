using System;
using System.Collections.Generic;
using System.Text;

namespace vs_extension.shared.Messages
{
    public sealed class NanoDeviceHasConnectedMessage
    {
        public string DeviceId { get; }

        public NanoDeviceHasConnectedMessage(string deviceId)
        {
            DeviceId = deviceId;
        }
    }
}
