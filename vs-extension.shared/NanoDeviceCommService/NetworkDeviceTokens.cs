//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Text;
using nanoFramework.Tools.Debugger.PortTcpIp;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// The tokens to connect to the network devices.
    /// </summary>
    /// <remarks>
    /// They are kept encrypted with DPAPI for the current user, never in plain text. An empty token means no authentication.
    /// </remarks>
    internal static class NetworkDeviceTokens
    {
        /// <summary>
        /// The maximum length of a token, in UTF-8 bytes.
        /// </summary>
        public static int MaxTokenLength => Commands.Monitor_Authenticate.MaxTokenLength;

        /// <summary>
        /// Gets the token to connect to a device.
        /// </summary>
        /// <param name="deviceId">The id of the device, tcpip://host:port.</param>
        /// <returns>The token, empty if there's none. Used as the token provider of the network devices.</returns>
        public static string GetDeviceToken(string deviceId)
        {
            return NanoSettings.GetProtectedValue(DeviceKey(deviceId)) ?? string.Empty;
        }

        /// <summary>
        /// Stores the token to connect to a device. An empty token deletes it.
        /// </summary>
        public static void SetDeviceToken(string deviceId, string token) => NanoSettings.SetProtectedValue(DeviceKey(deviceId), token);

        /// <summary>
        /// Deletes the token to connect to a device.
        /// </summary>
        public static void RemoveDeviceToken(string deviceId) => NanoSettings.SetProtectedValue(DeviceKey(deviceId), null);

        /// <summary>
        /// Checks the length of a token, that's limited in bytes and not in characters.
        /// </summary>
        /// <returns><see langword="null"/> if the token is OK, otherwise what's wrong with it.</returns>
        public static string Validate(string token)
        {
            if (!string.IsNullOrEmpty(token)
                && Encoding.UTF8.GetByteCount(token) > MaxTokenLength)
            {
                return $"The token can't be longer than {MaxTokenLength} bytes (UTF-8).";
            }

            return null;
        }

        // the same key whatever the way the address is written
        private static string DeviceKey(string deviceId) =>
            NetworkDeviceInformation.TryParse(deviceId, out var info) ? info.DeviceId : deviceId;
    }
}
