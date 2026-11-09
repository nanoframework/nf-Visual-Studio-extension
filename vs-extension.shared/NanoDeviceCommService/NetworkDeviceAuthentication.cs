//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Asks for the token of a network device that rejected the one it was given (or the lack of it), and tries again.
    /// </summary>
    internal static class NetworkDeviceAuthentication
    {
        private const int MaxAttempts = 3;

        private static readonly SemaphoreSlim s_promptLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Asks the user for the token of a device. Arguments are the id of the device, its description and
        /// <see langword="true"/> if it rejected a token it was given. Returns the token, <see langword="null"/> if the user cancelled.
        /// </summary>
        /// <remarks>
        /// Set by Device Explorer, that shows the dialog.
        /// </remarks>
        public static Func<string, string, bool, Task<string>> TokenPrompt { get; set; }

        /// <summary>
        /// Runs an operation that connects to <paramref name="device"/>. If a network device doesn't accept the token,
        /// the user is asked for another one, that's stored, and the operation is run again.
        /// </summary>
        /// <exception cref="DeviceAuthenticationFailedException">The user cancelled, or the token was wrong too many times.</exception>
        public static T Run<T>(NanoDeviceBase device, Func<T> operation)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return operation();
                }
                catch (DeviceAuthenticationFailedException ex)
                    when (device?.Transport == TransportType.TcpIp
                        && TokenPrompt != null
                        && attempt < MaxAttempts)
                {
                    // the connection of the failed attempt is dropped
                    device.DebugEngine?.Stop(true);

                    string token = Prompt(ex.DeviceId, device.Description, wasRejected: NetworkDeviceTokens.GetDeviceToken(ex.DeviceId).Length > 0);

                    if (token is null)
                    {
                        throw;
                    }

                    NetworkDeviceTokens.SetDeviceToken(ex.DeviceId, token);

                    // the device is listed as locked until it authenticates again, the connection itself is tried again below
                    try
                    {
                        device.RetryAuthentication(force: true);
                    }
                    catch (Exception)
                    {
                        // OK to catch all, the operation reports it if the device still doesn't accept the token
                    }
                }
            }
        }

        /// <summary>
        /// Asks the user for the token of a device, and stores it.
        /// </summary>
        /// <returns><see langword="false"/> if the user cancelled.</returns>
        public static bool PromptAndStore(string deviceId, string description, bool wasRejected)
        {
            string token = Prompt(deviceId, description, wasRejected);

            if (token is null)
            {
                return false;
            }

            NetworkDeviceTokens.SetDeviceToken(deviceId, token);

            return true;
        }

        private static string Prompt(string deviceId, string description, bool wasRejected)
        {
            Func<string, string, bool, Task<string>> prompt = TokenPrompt;

            if (prompt is null)
            {
                return null;
            }

            // one question at a time, e.g. a debug session and Device Explorer failing for the same device
            s_promptLock.Wait();

            try
            {
                return ThreadHelper.JoinableTaskFactory.Run(() => prompt(deviceId, description, wasRejected));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                s_promptLock.Release();
            }
        }
    }
}
