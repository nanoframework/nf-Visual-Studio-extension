//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension.Messages
{
    public sealed class NanoDeviceIsConnectedMessage
    {
        public string DeviceId { get; }

        public NanoDeviceIsConnectedMessage(string deviceId)
        {
            DeviceId = deviceId;
        }
    }
}
