//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// The settings of the extension, persisted per user in the Visual Studio Settings Store.
    /// </summary>
    /// <remarks>
    /// The values are read once and cached. They can be read and written from any thread.
    /// The options pages in Tools › Options and the Device Explorer toggles load from and save to this class.
    /// </remarks>
    internal static class NanoSettings
    {
        private const string CollectionPath = "nanoFramework";

        private static readonly object s_lock = new object();
        private static readonly Dictionary<string, object> s_values = new Dictionary<string, object>(StringComparer.Ordinal);
        private static WritableSettingsStore s_store;

        /// <summary>
        /// Raised after a setting has changed, with the name of its property. Can be raised on any thread.
        /// </summary>
        public static event PropertyChangedEventHandler SettingChanged;

        #region deployment

        /// <summary>
        /// Generate a deployment image on deploy. Default is <see langword="false"/>.
        /// </summary>
        public static bool GenerateDeploymentImage
        {
            get => Get(false);
            set => Set(value);
        }

        /// <summary>
        /// Include the configuration block in the deployment image. Default is <see langword="false"/>.
        /// </summary>
        public static bool IncludeConfigBlockInDeploymentImage
        {
            get => Get(false);
            set => Set(value);
        }

        /// <summary>
        /// Folder where the flash dumps are cached. Empty to cache them in the project output folder, the default.
        /// </summary>
        public static string FlashDumpCachePath
        {
            get => Get(string.Empty);
            set => Set(value ?? string.Empty);
        }

        /// <summary>
        /// Serial ports the device watchers ignore, separated by semicolons. Default is empty.
        /// </summary>
        public static string PortExclusionList
        {
            get => Get(string.Empty);
            set => Set(value ?? string.Empty);
        }

        /// <summary>
        /// The serial ports in <see cref="PortExclusionList"/>.
        /// </summary>
        public static List<string> GetPortExclusionList() => PortExclusionList
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        #endregion

        #region network devices

        /// <summary>
        /// Default TCP port of the network devices.
        /// </summary>
        public const int DefaultNetworkDevicePort = 26000;

        /// <summary>
        /// Default UDP port where the network devices announce themselves.
        /// </summary>
        public const int DefaultNetworkDiscoveryPort = 23657;

        /// <summary>
        /// Look for the network devices that announce themselves. Default is <see langword="true"/>.
        /// </summary>
        public static bool NetworkDiscoveryEnabled
        {
            get => Get(true);
            set => Set(value);
        }

        /// <summary>
        /// UDP port where the announcements of the network devices are received. Default is 23657.
        /// </summary>
        public static int NetworkDiscoveryPort
        {
            get => Get(DefaultNetworkDiscoveryPort);
            set => Set(value);
        }

        #endregion

        #region updates

        /// <summary>
        /// Update the firmware of the connected devices automatically. Default is <see langword="false"/>.
        /// </summary>
        public static bool AutoUpdateEnabled
        {
            get => Get(false);
            set => Set(value);
        }

        /// <summary>
        /// Include the preview firmware images in the updates. Default is <see langword="true"/>.
        /// </summary>
        public static bool IncludePrereleaseUpdates
        {
            get => Get(true);
            set => Set(value);
        }

        #endregion

        #region virtual device

        /// <summary>
        /// Run a virtual device (nanoCLR) on this machine. Default is <see langword="false"/>.
        /// </summary>
        public static bool VirtualDeviceEnabled
        {
            get => Get(false);
            set => Set(value);
        }

        /// <summary>
        /// Update the nanoCLR image of the virtual device when it's started with Visual Studio. Default is <see langword="true"/>.
        /// </summary>
        public static bool VirtualDeviceAutoUpdateNanoClr
        {
            get => Get(true);
            set => Set(value);
        }

        /// <summary>
        /// Load a local nanoCLR instance in the virtual device, from <see cref="VirtualDeviceLocalNanoClrPath"/>. Default is <see langword="false"/>.
        /// </summary>
        public static bool VirtualDeviceUseLocalNanoClr
        {
            get => Get(false);
            set => Set(value);
        }

        /// <summary>
        /// Path of the local nanoCLR instance (a DLL). Default is empty.
        /// </summary>
        public static string VirtualDeviceLocalNanoClrPath
        {
            get => Get(string.Empty);
            set => Set(value ?? string.Empty);
        }

        /// <summary>
        /// Serial port of the virtual device. Empty to use the first virtual serial port, the default.
        /// </summary>
        public static string VirtualDevicePort
        {
            get => Get(string.Empty);
            set => Set(value ?? string.Empty);
        }

        public const string VirtualDeviceTransportSerial = "Serial";

        public const string VirtualDeviceTransportNetwork = "Network";

        /// <summary>
        /// How the virtual device is connected: "Serial" (a virtual serial port pair, the default) or "Network" (TCP).
        /// </summary>
        public static string VirtualDeviceTransport
        {
            get => Get(VirtualDeviceTransportSerial);
            set => Set(value ?? VirtualDeviceTransportSerial);
        }

        /// <summary>
        /// The virtual device is connected through the network.
        /// </summary>
        public static bool VirtualDeviceUsesNetwork => string.Equals(VirtualDeviceTransport, VirtualDeviceTransportNetwork, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// TCP port where the virtual device listens in network mode. Default is 26000.
        /// </summary>
        public static int VirtualDeviceNetworkPort
        {
            get => Get(DefaultNetworkDevicePort);
            set => Set(value);
        }

        /// <summary>
        /// UDP port where the virtual device announces itself in network mode. Default is 23657.
        /// </summary>
        public static int VirtualDeviceDiscoveryPort
        {
            get => Get(DefaultNetworkDiscoveryPort);
            set => Set(value);
        }

        #endregion

        #region debugging

        /// <summary>
        /// The device processes the stack trace of the exceptions. Default is <see langword="true"/>.
        /// </summary>
        public static bool ProcessStackTrace
        {
            get => Get(true);
            set => Set(value);
        }

        #endregion

        #region Device Explorer

        /// <summary>
        /// Output the internal errors of the extension to the Output window. Default is <see langword="false"/>.
        /// </summary>
        public static bool ShowInternalErrors
        {
            get => Get(false);
            set => Set(value);
        }

        /// <summary>
        /// The device watchers are disabled, so no device is found. Default is <see langword="false"/>.
        /// </summary>
        public static bool DeviceWatchersDisabled
        {
            get => Get(false);
            set => Set(value);
        }

        #endregion

        #region protected values

        private const string SecretsCollectionPath = "nanoFramework\\Secrets";

        // not a secret, only makes the protected values unusable for other uses of DPAPI by the same user
        private static readonly byte[] s_secretEntropy = Encoding.UTF8.GetBytes("nanoFramework.VisualStudio.Extension");

        /// <summary>
        /// Gets a value stored with <see cref="SetProtectedValue"/>.
        /// </summary>
        /// <param name="key">Identifies the value. It's not stored as is.</param>
        /// <returns>The value, <see langword="null"/> if there's none or it can't be decrypted (e.g. stored by another user).</returns>
        public static string GetProtectedValue(string key)
        {
            lock (s_lock)
            {
                try
                {
                    string name = SecretName(key);

                    if (s_store is null
                        || !s_store.CollectionExists(SecretsCollectionPath)
                        || !s_store.PropertyExists(SecretsCollectionPath, name))
                    {
                        return null;
                    }

                    byte[] protectedBytes = Convert.FromBase64String(s_store.GetString(SecretsCollectionPath, name));

                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, s_secretEntropy, DataProtectionScope.CurrentUser));
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Stores a value encrypted with DPAPI for the current user, never in plain text. A <see langword="null"/> or empty value deletes it.
        /// </summary>
        /// <param name="key">Identifies the value. It's not stored as is.</param>
        public static void SetProtectedValue(string key, string value)
        {
            lock (s_lock)
            {
                if (s_store is null)
                {
                    return;
                }

                string name = SecretName(key);

                try
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        if (s_store.CollectionExists(SecretsCollectionPath)
                            && s_store.PropertyExists(SecretsCollectionPath, name))
                        {
                            s_store.DeleteProperty(SecretsCollectionPath, name);
                        }

                        return;
                    }

                    if (!s_store.CollectionExists(SecretsCollectionPath))
                    {
                        s_store.CreateCollection(SecretsCollectionPath);
                    }

                    byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), s_secretEntropy, DataProtectionScope.CurrentUser);

                    s_store.SetString(SecretsCollectionPath, name, Convert.ToBase64String(protectedBytes));
                }
                catch (Exception ex)
                {
                    // the value isn't kept
                    MessageCentre.InternalErrorWriteLine($"Error storing a protected setting: {ex.Message}");
                }
            }
        }

        // the keys (device ids) aren't kept either, only their hash
        private static string SecretName(string key)
        {
            using var sha = SHA256.Create();

            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToLowerInvariant()))).Replace("-", string.Empty);
        }

        #endregion

        /// <summary>
        /// Opens the Settings Store. Until then, the settings have their default values and changes aren't persisted.
        /// </summary>
        /// <param name="serviceProvider">The package.</param>
        public static void Initialize(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settingsManager = new ShellSettingsManager(serviceProvider);
            var store = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (!store.CollectionExists(CollectionPath))
            {
                store.CreateCollection(CollectionPath);
            }

            lock (s_lock)
            {
                s_store = store;

                // anything read before is read again from the store
                s_values.Clear();
            }
        }

        private static T Get<T>(T defaultValue, [CallerMemberName] string name = null)
        {
            lock (s_lock)
            {
                if (s_values.TryGetValue(name, out object value))
                {
                    return (T)value;
                }

                if (s_store is null)
                {
                    return defaultValue;
                }

                T storedValue = ReadFromStore<T>(name) is T read ? read : defaultValue;

                s_values[name] = storedValue;

                return storedValue;
            }
        }

        private static void Set<T>(T value, [CallerMemberName] string name = null)
        {
            lock (s_lock)
            {
                object current = s_values.TryGetValue(name, out object cached) ? cached : ReadFromStore<T>(name);

                if (Equals(current, value))
                {
                    return;
                }

                s_values[name] = value;

                try
                {
                    if (value is bool boolValue)
                    {
                        s_store?.SetBoolean(CollectionPath, name, boolValue);
                    }
                    else if (value is int intValue)
                    {
                        s_store?.SetInt32(CollectionPath, name, intValue);
                    }
                    else
                    {
                        s_store?.SetString(CollectionPath, name, (string)(object)value);
                    }
                }
                catch
                {
                    // the value is kept for this session
                }
            }

            SettingChanged?.Invoke(null, new PropertyChangedEventArgs(name));
        }

        // the stored value, null if there's none
        private static object ReadFromStore<T>(string name)
        {
            try
            {
                if (s_store is null
                    || !s_store.PropertyExists(CollectionPath, name))
                {
                    return null;
                }

                if (typeof(T) == typeof(bool))
                {
                    return s_store.GetBoolean(CollectionPath, name);
                }

                if (typeof(T) == typeof(int))
                {
                    return s_store.GetInt32(CollectionPath, name);
                }

                return s_store.GetString(CollectionPath, name);
            }
            catch
            {
                // a value of the wrong type, same as none
                return null;
            }
        }
    }
}
