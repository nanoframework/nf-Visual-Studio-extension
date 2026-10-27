//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Telemetry start-up and shut-down, called by <see cref="NanoFrameworkPackage"/>.
    /// </summary>
    internal static class TelemetryStartup
    {
        /// <summary>
        /// Builds the telemetry pipeline. Call early in the package initialization, from a background thread.
        /// </summary>
        internal static void Initialize()
        {
            try
            {
                TelemetrySetup.Initialize(
                    TelemetryConnection.Get(),
                    GetInformationalVersion(),
                    GetVisualStudioVersion());
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// Shows the telemetry notice if needed and then records the <c>ExtensionLoaded</c> event.
        /// Call once the package and the output panes are initialized.
        /// </summary>
        internal static async Task OnPackageLoadedAsync(AsyncPackage package)
        {
            if (!ExtensionTelemetry.IsEnabled)
            {
                return;
            }

            // show the notice before sending anything
            await TelemetryNotice.ShowIfNeededAsync(package, NanoFrameworkPackage.NanoFrameworkExtensionVersion);

            await TaskScheduler.Default;

            try
            {
                TelemetryProperties settings = GetSettingsSnapshot();

                try
                {
                    settings.Add("process_stacktrace", NanoFrameworkPackage.DebuggingOptions?.ProcessStackTraceOption);
                }
                catch
                {
                    // option page not available
                }

                ExtensionTelemetry.TrackEvent("ExtensionLoaded", settings);

                // projects are usually loaded before the package
                ProjectLoadTelemetry.SendPendingEvents();
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// Gets the extension settings, as booleans only: settings holding paths or port names are recorded as set or not.
        /// </summary>
        internal static TelemetryProperties GetSettingsSnapshot() => new TelemetryProperties
        {
            { "auto_update", NanoFrameworkPackage.SettingAutoUpdateEnable },
            { "prerelease_updates", NanoFrameworkPackage.SettingIncludePrereleaseUpdates },
            { "virtual_device", NanoFrameworkPackage.SettingVirtualDeviceEnable },
            { "vd_autoupdate_clr", NanoFrameworkPackage.SettingVirtualDeviceAutoUpdateNanoClrImage },
            { "vd_custom_clr", NanoFrameworkPackage.SettingLoadNanoClrInstance },
            { "vd_port_set", !string.IsNullOrEmpty(NanoFrameworkPackage.SettingVirtualDevicePort) },
            { "deployment_image", NanoFrameworkPackage.SettingGenerateDeploymentImage },
            { "config_block_in_image", NanoFrameworkPackage.SettingIncludeConfigBlockInDeploymentImage },
            { "flash_dump_path_set", !string.IsNullOrEmpty(NanoFrameworkPackage.SettingPathOfFlashDumpCache) },
            { "disable_watchers", NanoFrameworkPackage.OptionDisableDeviceWatchers },
            { "show_internal_errors", NanoFrameworkPackage.OptionShowInternalErrors },
            { "port_blacklist_set", !string.IsNullOrEmpty(NanoFrameworkPackage.SettingPortBlackList) },
        };

        /// <summary>
        /// Records a <c>SettingChanged</c> event for each setting that differs between two snapshots
        /// taken with <see cref="GetSettingsSnapshot"/>.
        /// </summary>
        internal static void TrackSettingsChanges(TelemetryProperties before, TelemetryProperties after)
        {
            if (before is null || after is null)
            {
                return;
            }

            try
            {
                foreach (System.Collections.Generic.KeyValuePair<string, object> setting in after)
                {
                    object previous = before.Find(s => s.Key == setting.Key).Value;

                    if (!Equals(previous, setting.Value))
                    {
                        ExtensionTelemetry.TrackEvent("SettingChanged", new TelemetryProperties
                        {
                            { "setting", setting.Key },
                            { "value", setting.Value },
                        });
                    }
                }
            }
            catch
            {
                // telemetry must never break the extension
            }
        }

        /// <summary>
        /// Flushes pending telemetry and tears down the pipeline. Call when Visual Studio is closing.
        /// </summary>
        internal static void Shutdown() => TelemetrySetup.Shutdown(TimeSpan.FromSeconds(2));

        private static string GetInformationalVersion()
        {
            try
            {
                return typeof(TelemetryStartup).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the Visual Studio major.minor version from devenv.exe, which doesn't require the UI thread.
        /// </summary>
        private static string GetVisualStudioVersion()
        {
            try
            {
                FileVersionInfo versionInfo = Process.GetCurrentProcess().MainModule.FileVersionInfo;

                return $"{versionInfo.FileMajorPart}.{versionInfo.FileMinorPart}";
            }
            catch
            {
                return null;
            }
        }
    }
}
