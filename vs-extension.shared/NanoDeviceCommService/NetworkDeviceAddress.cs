//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Checks for the ports of the network devices.
    /// </summary>
    internal static class NetworkDeviceAddress
    {
        /// <summary>
        /// Checks that a port is valid.
        /// </summary>
        public static bool IsValidPort(int port) => port >= 1 && port <= 65535;
    }
}
