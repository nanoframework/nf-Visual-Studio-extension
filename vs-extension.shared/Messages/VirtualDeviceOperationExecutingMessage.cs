//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension.Messages
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
