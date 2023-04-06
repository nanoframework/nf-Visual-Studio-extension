using System;
using System.Collections.Generic;
using System.Text;

namespace vs_extension.shared.Messages
{
    public sealed class VirtualDeviceOperationExecutingMessage
    {
        public bool InstallCompleted { get; }

        public VirtualDeviceOperationExecutingMessage(bool installCompleted) 
        {
            InstallCompleted = installCompleted;
        }
    }
}
