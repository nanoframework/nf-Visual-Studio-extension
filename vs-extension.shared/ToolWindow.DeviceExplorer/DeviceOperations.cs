//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.NFDevice;
using nanoFramework.Tools.Debugger.WireProtocol;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using static nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel.DeviceExplorerViewModel.Messages;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// The Device Explorer operations on the selected device, and the rules that enable them.
    /// </summary>
    /// <remarks>
    /// This class doesn't depend on any UI: progress and results are reported through <see cref="MessageCentre"/>,
    /// the changes to the enable rules through the <see cref="StateChanged"/> event.
    /// The device state is owned by <see cref="NanoFrameworkPackage.DeviceExplorerViewModel"/>.
    /// </remarks>
    internal static class DeviceOperations
    {
        // Timeout for exclusive access
        private const int ExclusiveAccessTimeout = 3000;

        private static readonly object s_messageRecipient = new object();

        private static INanoDeviceCommService s_nanoDeviceCommService;

        private static IVirtualDeviceService s_virtualDeviceService;

        // the virtual device is started and stopped one operation at a time
        private static readonly SemaphoreSlim s_virtualDeviceLock = new SemaphoreSlim(1, 1);

        private static int s_runningVirtualDeviceOperations;

        // set while a virtual device that failed to start is disabled again
        [ThreadStatic]
        private static bool s_revertingVirtualDeviceEnabled;

        private static int s_runningOperations;

        private static string s_operationText;

        private static bool s_isEnumerating;

        // what was read from each device, so it's read only once
        private static readonly Dictionary<NanoDeviceBase, DeviceCapabilitiesData> s_capabilitiesCache = new Dictionary<NanoDeviceBase, DeviceCapabilitiesData>();

        /// <summary>
        /// Raised when the state used by the enable rules has changed: devices, selection, running operations, enumeration or options.
        /// </summary>
        /// <remarks>Can be raised on any thread.</remarks>
        public static event EventHandler StateChanged;


        private static DeviceExplorerViewModel ViewModel => NanoFrameworkPackage.DeviceExplorerViewModel;

        /// <summary>
        /// Text describing the operation that is running, <see langword="null"/> if there is none.
        /// </summary>
        public static string OperationText => Volatile.Read(ref s_operationText);

        /// <summary>
        /// <see langword="true"/> while an operation on the selected device is running.
        /// </summary>
        public static bool IsBusy => Volatile.Read(ref s_runningOperations) > 0;

        /// <summary>
        /// <see langword="true"/> while the device watchers are looking for devices.
        /// </summary>
        public static bool IsEnumerating => Volatile.Read(ref s_isEnumerating);

        /// <summary>
        /// Sets up the device communication service and the listeners for the Device Explorer view model.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            s_nanoDeviceCommService = await package.GetServiceAsync(typeof(NanoDeviceCommService)) as INanoDeviceCommService;

            // setup message listeners to be notified of events occurring in the View Model
            // before the watchers are started, so the first messages aren't missed
            WeakReferenceMessenger.Default.Register<SelectedNanoDeviceHasChangedMessage>(s_messageRecipient, static (r, message) => OnSelectedDeviceChanged());
            WeakReferenceMessenger.Default.Register<NanoDevicesCollectionHasChangedMessage>(s_messageRecipient, static (r, message) => RaiseStateChanged());
            WeakReferenceMessenger.Default.Register<NanoDeviceEnumerationCompletedMessage>(s_messageRecipient, static (r, message) => OnDeviceEnumerationCompleted());

            // this starts the device watchers
            ViewModel.NanoDeviceCommService = s_nanoDeviceCommService;

            s_isEnumerating = !NanoSettings.DeviceWatchersDisabled
                && !s_nanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete;

            s_virtualDeviceService = await package.GetServiceAsync(typeof(VirtualDeviceService)) as IVirtualDeviceService;

            if (s_virtualDeviceService != null)
            {
                s_virtualDeviceService.StateChanged += static (sender, e) => RaiseStateChanged();
            }

            // the virtual device follows its setting, changed from Device Explorer or from the options page
            NanoSettings.SettingChanged += OnSettingChanged;
        }

        #region enable rules

        /// <summary>
        /// A device is present and selected, and no other operation is running.
        /// </summary>
        public static bool CanRunDeviceOperation =>
            !IsBusy
            && ViewModel?.SelectedDevice != null
            && ViewModel.AvailableDevicesSnapshot.Count > 0;

        /// <summary>
        /// Checks if the selected device can be rebooted with <paramref name="rebootOption"/>.
        /// </summary>
        /// <param name="rebootOption">The reboot option. <see langword="null"/> is the default reboot, chosen after connecting to the device.</param>
        public static bool CanReboot(RebootOptions? rebootOption)
        {
            if (!CanRunDeviceOperation)
            {
                return false;
            }

            var device = ViewModel.SelectedDevice;

            return rebootOption switch
            {
                // reboot nanoBooter, if available on target
                RebootOptions.EnterNanoBooter => device.HasNanoBooter,

                // reboot to bootloader, if available on target
                RebootOptions.EnterProprietaryBooter => device.HasProprietaryBooter,

                // reboot CLR if we are on CLR
                RebootOptions.ClrOnly => device.DebugEngine?.IsConnectedTonanoCLR ?? false,

                _ => true,
            };
        }

        /// <summary>
        /// Devices can be rescanned when the watchers are enabled and not already looking for devices.
        /// </summary>
        public static bool CanRescan => !NanoSettings.DeviceWatchersDisabled && !IsEnumerating;

        /// <summary>
        /// <see langword="true"/> while the virtual device is being set up, started or stopped, here or when Visual Studio starts.
        /// </summary>
        public static bool IsVirtualDeviceBusy =>
            Volatile.Read(ref s_runningVirtualDeviceOperations) > 0
            || (s_virtualDeviceService?.IsOperationRunning ?? false);

        /// <summary>
        /// The virtual device is running.
        /// </summary>
        public static bool IsVirtualDeviceRunning => s_virtualDeviceService?.VirtualDeviceIsRunning ?? false;

        /// <summary>
        /// The virtual device can be enabled or disabled: nothing is being done to it.
        /// </summary>
        public static bool CanToggleVirtualDevice =>
            s_virtualDeviceService != null
            && s_virtualDeviceService.CanStartStopVirtualDevice
            && !IsVirtualDeviceBusy;

        /// <summary>
        /// The virtual device can be restarted, or its nanoCLR updated: it's enabled and nothing is being done to it.
        /// </summary>
        public static bool CanRunVirtualDeviceOperation => CanToggleVirtualDevice && NanoSettings.VirtualDeviceEnabled;

        #endregion

        #region device operations

        /// <summary>
        /// Pings the selected device.
        /// </summary>
        public static Task PingAsync()
        {
            return RunDeviceOperationAsync(
                "deviceexplorer.ping",
                "Pinging {0}...",
                connect: true,
                (device, description, telemetry) =>
                {
                    // ping device
                    var reply = device.Ping();

                    telemetry
                        .SetProperty("ping_result", reply)
                        .Complete(reply == ConnectionSource.Unknown ? "NoReply" : TelemetryOperation.ResultOk);

                    if (reply == ConnectionSource.nanoBooter)
                    {
                        Report($"{description} is active running nanoBooter.");
                    }
                    else if (reply == ConnectionSource.nanoCLR)
                    {
                        Report($"{description} is active running nanoCLR.");
                    }
                    else
                    {
                        Report($"No reply from {description}", isWarning: true);
                    }

                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Reads the capabilities of the selected device, stores them in the view model and outputs them.
        /// </summary>
        public static Task QueryCapabilitiesAsync()
        {
            var deviceExplorer = ViewModel;

            return RunDeviceOperationAsync(
                "deviceexplorer.capabilities",
                "Querying {0} capabilities...",
                connect: false,
                (device, description, telemetry) =>
                {
                    if (!ConnectAndReadCapabilities(device, deviceExplorer))
                    {
                        Report($"Error retrieving device information from {description}. Please reconnect device.", isWarning: true);

                        telemetry.Complete("DeviceInfoFailed");

                        return Task.CompletedTask;
                    }

                    telemetry
                        .SetProperty("connection_source", deviceExplorer.LastDeviceConnectionSource)
                        .Complete(TelemetryOperation.ResultOk);

                    OutputCapabilities(deviceExplorer);

                    Report($"{description} capabilities are in the Output window.");

                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Reads the capabilities of the selected device, unless they were already read. Nothing is reported, this only fills the cache.
        /// </summary>
        public static Task ReadCapabilitiesIfNeededAsync()
        {
            var deviceExplorer = ViewModel;
            var selected = deviceExplorer?.SelectedDevice;

            if (selected is null || GetCachedCapabilitiesData(selected) != null)
            {
                return Task.CompletedTask;
            }

            return RunDeviceOperationAsync(
                "deviceexplorer.readdetails",
                "Reading {0} details...",
                connect: false,
                (device, description, telemetry) =>
                {
                    bool read = ConnectAndReadCapabilities(device, deviceExplorer);

                    telemetry.Complete(read ? TelemetryOperation.ResultOk : "DeviceInfoFailed");

                    return Task.CompletedTask;
                },
                quiet: true);
        }

        /// <summary>
        /// Forgets what was read from the devices, after it was changed: deployment, firmware update, erase or reboot.
        /// </summary>
        /// <param name="device">The device that was changed, <see langword="null"/> for all devices.</param>
        public static void InvalidateCapabilities(NanoDeviceBase device = null)
        {
            lock (s_capabilitiesCache)
            {
                if (device is null)
                {
                    s_capabilitiesCache.Clear();
                }
                else
                {
                    s_capabilitiesCache.Remove(device);
                }
            }
        }

        /// <summary>
        /// Erases the deployment area of the selected device.
        /// </summary>
        public static Task EraseDeploymentAsync()
        {
            var deviceExplorer = ViewModel;

            var logProgressIndicator = new Progress<string>(MessageCentre.InternalErrorWriteLine);
            var progressIndicator = new Progress<MessageWithProgress>((m) => MessageCentre.StartMessageWithProgress(m));

            return RunDeviceOperationAsync(
                "deviceexplorer.erase",
                "Erasing {0} deployment area...",
                connect: false,
                (device, description, telemetry) =>
                {
                    // connect to the device
                    if (!device.DebugEngine.Connect(false, true))
                    {
                        // reset property to force that device capabilities are retrieved on next connection
                        deviceExplorer.LastDeviceConnectedHash = 0;

                        ReportNotResponding(description, telemetry);

                        return Task.CompletedTask;
                    }

                    try
                    {
                        if (device.Erase(
                            EraseOptions.Deployment,
                            progressIndicator,
                            logProgressIndicator))
                        {
                            Report($"{description} deployment area erased.");

                            telemetry.Complete(TelemetryOperation.ResultOk);

                            // reset the hash for the connected device so the deployment information can be refreshed, if and when requested
                            deviceExplorer.LastDeviceConnectedHash = 0;
                            InvalidateCapabilities(device);
                        }
                        else
                        {
                            // report issue to user
                            Report($"Error erasing {description} deployment area.", isWarning: true);

                            telemetry.Complete(TelemetryOperation.ResultFailed);
                        }
                    }
                    catch (Exception ex)
                    {
                        telemetry.Fail(ex, "deviceexplorer.erase");

                        // report issue to user
                        Report($"Error erasing {description} deployment area.", isWarning: true);
                    }

                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Reboots the selected device.
        /// </summary>
        /// <param name="rebootOption">The reboot option. <see langword="null"/> reboots the CLR when the device is running it, otherwise does a normal reboot.</param>
        public static Task RebootAsync(RebootOptions? rebootOption)
        {
            var deviceExplorer = ViewModel;

            return RunDeviceOperationAsync(
                "deviceexplorer.reboot",
                "Rebooting {0}...",
                connect: false,
                (device, description, telemetry) =>
                {
                    // connect to the device
                    if (!device.DebugEngine.Connect(false, true))
                    {
                        // reset property to force that device capabilities are retrieved on next connection
                        deviceExplorer.LastDeviceConnectedHash = 0;

                        ReportNotResponding(description, telemetry);

                        return Task.CompletedTask;
                    }

                    try
                    {
                        // reset the hash for the connected device so the deployment information can be refreshed, if and when requested
                        deviceExplorer.LastDeviceConnectedHash = 0;

                        // what's running on the device changes
                        InvalidateCapabilities(device);

                        // the default reboot option is the expected one for the current state of the device
                        var option = rebootOption
                            ?? (device.DebugEngine.IsConnectedTonanoCLR ? RebootOptions.ClrOnly : RebootOptions.NormalReboot);

                        MessageCentre.OutputMessage($"Sending reboot command to {description}.");

                        device.DebugEngine.RebootDevice(option);

                        telemetry
                            .SetProperty("reboot_type", option)
                            .Complete(TelemetryOperation.ResultOk);

                    }
                    catch (Exception ex)
                    {
                        telemetry.Fail(ex, "deviceexplorer.reboot");

                        // report issue to user
                        Report($"Error sending reboot command to {description}.", isWarning: true);
                    }

                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Reads the network configuration of the selected device into the view model and calls <paramref name="editConfiguration"/>
        /// on the main thread, while the device is still connected, so the configuration can be edited and applied.
        /// </summary>
        /// <param name="editConfiguration">Shows the editor for the network configuration. It's called on the main thread.</param>
        public static Task EditNetworkConfigurationAsync(Action editConfiguration)
        {
            var deviceExplorer = ViewModel;

            return RunDeviceOperationAsync(
                "deviceexplorer.networkconfig",
                "Reading {0} network configuration...",
                connect: false,
                async (device, description, telemetry) =>
                {
                    // connect to the device
                    if (!device.DebugEngine.Connect(false, true))
                    {
                        ReportNotResponding(description, telemetry);

                        return;
                    }

                    try
                    {
                        // for now, just get the 1st network configuration, if exists
                        var networkConfigurations = device.DebugEngine.GetAllNetworkConfigurations();

                        deviceExplorer.DeviceNetworkConfiguration = new NetworkConfigurationViewModel(
                            networkConfigurations.Count > 0
                                ? networkConfigurations[0]
                                : new DeviceConfiguration.NetworkConfigurationProperties());

                        // for now, just get the 1st Wi-Fi configuration, if exists
                        var wirellesConfigurations = device.DebugEngine.GetAllWireless80211Configurations();

                        deviceExplorer.DeviceWireless80211Configuration = new Wireless80211ConfigurationViewModel(
                            wirellesConfigurations.Count > 0
                                ? wirellesConfigurations[0]
                                : new DeviceConfiguration.Wireless80211ConfigurationProperties());

                        if (networkConfigurations.Count == 0
                           && wirellesConfigurations.Count == 0)
                        {
                            // no network configuration available: device doesn't have network capabilities
                            Report($"{description} doesn't have network capabilities. Can't open Network Configuration dialog.", isWarning: true);

                            telemetry.Complete("NoNetworkCapabilities", true);

                            // done here
                            return;
                        }

                        // need to switch to UI main thread
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // the progress messages are cleared while the user is editing, the operation still runs to keep the device connected
                        MessageCentre.StopProgressMessage();
                        SetOperationText(null);

                        editConfiguration();

                        telemetry
                            .SetProperty("wireless", wirellesConfigurations.Count > 0)
                            .Complete(TelemetryOperation.ResultOk);
                    }
                    catch (Exception ex)
                    {
                        telemetry.Fail(ex, "deviceexplorer.networkconfig");

                        // report issue to user
                        Report($"Error reading {description} configurations.", isWarning: true);
                    }
                });
        }

        /// <summary>
        /// Uploads the network configuration being edited in the view model to the selected device.
        /// Has to be called from the editor shown by <see cref="EditNetworkConfigurationAsync"/>, while the device is connected.
        /// </summary>
        /// <param name="networkConfigurationToSave">The network configuration block to save.</param>
        /// <returns><see langword="true"/> if the configuration was saved.</returns>
        public static bool ApplyNetworkConfiguration(DeviceConfiguration.NetworkConfigurationProperties networkConfigurationToSave)
        {
            var deviceExplorer = ViewModel;
            var device = deviceExplorer.SelectedDevice;

            MessageCentre.StartProgressMessage($"Uploading network configuration to {device.Description}...");

            // check if debugger engine exists
            if (device.DebugEngine == null)
            {
                device.CreateDebugEngine();
            }

            // upload step being executed and certificates successfully uploaded, for telemetry
            string uploadStep = "network";
            bool rootCaUploaded = false;
            bool deviceCertificateUploaded = false;

            // save network configuration to target
            var updateResult = device.DebugEngine.UpdateDeviceConfiguration(networkConfigurationToSave, 0);

            if (updateResult == Engine.UpdateDeviceResult.Sucess)
            {
                if (deviceExplorer.DeviceNetworkConfiguration.InterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    // save Wi-Fi profile to target
                    uploadStep = "wireless";
                    updateResult = device.DebugEngine.UpdateDeviceConfiguration(deviceExplorer.DeviceWireless80211Configuration.Model, 0);
                }
            }

            if (updateResult != Engine.UpdateDeviceResult.Sucess)
            {
                // update failed
                MessageCentre.OutputMessage($"Error updating {device.Description} network configuration. Error: {updateResult}.");
                MessageCentre.StopProgressMessage();

                TrackNetworkConfigSaved(networkConfigurationToSave, uploadStep, updateResult, rootCaUploaded, deviceCertificateUploaded);

                return false;
            }

            // update of network config successful
            MessageCentre.OutputMessage($"{device.Description} network configuration updated.");

            // is there a CA certificate bundle to upload?
            if (deviceExplorer.CaCertificateBundle != null)
            {
                MessageCentre.StartProgressMessage($"Uploading Root CA file to {device.Description}...");

                // save Root CA file to target
                // at position 0
                updateResult = device.DebugEngine.UpdateDeviceConfiguration(deviceExplorer.CaCertificateBundle, 0);

                if (updateResult != Engine.UpdateDeviceResult.Sucess)
                {
                    MessageCentre.OutputMessage($"Error uploading Root CA file to {device.Description}. Error: {updateResult}");
                    MessageCentre.StopProgressMessage();

                    TrackNetworkConfigSaved(networkConfigurationToSave, "rootCa", updateResult, rootCaUploaded, deviceCertificateUploaded);

                    return false;
                }

                rootCaUploaded = true;
            }

            // is there a device certificate to upload?
            if (deviceExplorer.DeviceCertificate != null)
            {
                MessageCentre.StartProgressMessage($"Uploading device certificate file to {device.Description}...");

                // save device certificate file to target
                // at position 0
                updateResult = device.DebugEngine.UpdateDeviceConfiguration(deviceExplorer.DeviceCertificate, 0);

                if (updateResult != Engine.UpdateDeviceResult.Sucess)
                {
                    MessageCentre.OutputMessage($"Error uploading device certificate file to {device.Description}. Error: {updateResult}");
                    MessageCentre.StopProgressMessage();

                    TrackNetworkConfigSaved(networkConfigurationToSave, "deviceCertificate", updateResult, rootCaUploaded, deviceCertificateUploaded);

                    return false;
                }

                deviceCertificateUploaded = true;
            }

            // stop progress message
            MessageCentre.StopProgressMessage();

            TrackNetworkConfigSaved(networkConfigurationToSave, null, updateResult, rootCaUploaded, deviceCertificateUploaded);


            return true;
        }

        #endregion

        #region device watchers and options

        /// <summary>
        /// Rescans the devices.
        /// </summary>
        public static void RescanDevices()
        {
            if (!CanRescan)
            {
                return;
            }

            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "rescan" } });

            // the rescan is enabled again when the device enumeration completes
            SetEnumerating(true);

            try
            {
                s_nanoDeviceCommService.DebugClient.ReScanDevices();

                MessageCentre.StartProgressMessage(MessageCentre.MessageStartSearchingDevices);
            }
            catch (Exception ex)
            {
                // empty on purpose, exceptions can happen during rescan
                ExtensionTelemetry.TrackException(ex, "deviceexplorer.rescan");

                // OK to enable rescan here, in case an exception occurs
                SetEnumerating(false);
            }
        }

        /// <summary>
        /// Enables or disables the device watchers. The value is persisted per user.
        /// </summary>
        public static void SetDeviceWatchersDisabled(bool disabled)
        {
            if (NanoSettings.DeviceWatchersDisabled == disabled)
            {
                return;
            }

            // save new status
            NanoSettings.DeviceWatchersDisabled = disabled;

            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties
            {
                { "command", "disableDeviceWatchers" },
                { "value", disabled },
            });

            // call device port API
            if (!disabled)
            {
                MessageCentre.InternalErrorWriteLine("Starting device watchers");

                // rescan is enabled again when the device enumeration completes
                SetEnumerating(true);

                s_nanoDeviceCommService.DebugClient.StartDeviceWatchers();

                MessageCentre.StartProgressMessage(MessageCentre.MessageStartSearchingDevices);
            }
            else
            {
                MessageCentre.InternalErrorWriteLine("Stopping device watchers");

                // not searching anymore, even if stopping the watchers takes a while
                SetEnumerating(false);

                s_nanoDeviceCommService.DebugClient.StopDeviceWatchers();

                ViewModel.ResetEnumerationFlag();

                MessageCentre.OutputMessage(Environment.NewLine);
                MessageCentre.OutputMessage("***********************************************************************************");
                MessageCentre.OutputMessage("** Device Watchers are DISABLED. You won't be able to connect to any nanoDevice. **");
                MessageCentre.OutputMessage("***********************************************************************************");
                MessageCentre.OutputMessage(Environment.NewLine);
            }
        }

        /// <summary>
        /// Shows or hides the internal errors of the extension in the output pane. The value is persisted per user.
        /// </summary>
        public static void SetShowInternalErrors(bool show)
        {
            if (NanoSettings.ShowInternalErrors == show)
            {
                return;
            }

            // save new status
            NanoSettings.ShowInternalErrors = show;

            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties
            {
                { "command", "showInternalErrors" },
                { "value", show },
            });
        }

        #endregion

        #region virtual device

        /// <summary>
        /// Enables or disables the virtual device. The value is persisted per user.
        /// Enabling it starts it, installing or updating the nanoclr tool first if needed. Disabling it stops it.
        /// </summary>
        public static void SetVirtualDeviceEnabled(bool enabled)
        {
            if (NanoSettings.VirtualDeviceEnabled == enabled)
            {
                return;
            }

            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties
            {
                { "command", "virtualDevice" },
                { "value", enabled },
            });

            // the virtual device is started or stopped when the setting changes
            NanoSettings.VirtualDeviceEnabled = enabled;
        }

        /// <summary>
        /// Stops the virtual device and starts it again.
        /// </summary>
        public static Task RestartVirtualDeviceAsync()
        {
            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "restartVirtualDevice" } });

            return RunVirtualDeviceOperationAsync(
                "Restarting the virtual device...",
                async service =>
                {
                    if (service.VirtualDeviceIsRunning)
                    {
                        service.StopVirtualDevice();
                    }

                    await service.StartVirtualDeviceAsync(true, "deviceexplorer.restart");
                });
        }

        /// <summary>
        /// Updates the nanoclr tool and the nanoCLR image of the virtual device. A running virtual device is stopped during the update and started again.
        /// </summary>
        public static Task UpdateNanoClrAsync()
        {
            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "updateNanoClr" } });

            return RunVirtualDeviceOperationAsync(
                "Updating nanoCLR...",
                async service =>
                {
                    bool wasRunning = service.VirtualDeviceIsRunning;

                    if (wasRunning)
                    {
                        service.StopVirtualDevice();
                    }

                    service.InstallNanoClrTool();

                    if (service.NanoClrIsInstalled)
                    {
                        service.UpdateNanoClr();
                    }

                    if (wasRunning)
                    {
                        await service.StartVirtualDeviceAsync(true, "deviceexplorer.update");
                    }
                });
        }

        private static void OnSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NanoSettings.VirtualDeviceEnabled)
                && !s_revertingVirtualDeviceEnabled)
            {
                _ = ApplyVirtualDeviceEnabledAsync();
            }

            RaiseStateChanged();
        }

        // starts or stops the virtual device, to match its setting
        private static Task ApplyVirtualDeviceEnabledAsync()
        {
            bool enable = NanoSettings.VirtualDeviceEnabled;

            return RunVirtualDeviceOperationAsync(
                enable ? "Starting the virtual device..." : "Stopping the virtual device...",
                async service =>
                {
                    // the setting can have changed again while waiting for the previous operation
                    enable = NanoSettings.VirtualDeviceEnabled;

                    if (enable && !service.VirtualDeviceIsRunning)
                    {
                        service.InstallNanoClrTool();

                        if (!service.NanoClrIsInstalled)
                        {
                            MessageCentre.OutputVirtualDeviceMessage("ERROR: the nanoclr tool isn't installed, the virtual device can't be started.");
                        }

                        if (!service.NanoClrIsInstalled
                            || !await service.StartVirtualDeviceAsync(true, "deviceexplorer"))
                        {
                            // the toggle shows that it isn't running, the reason is in the Output window
                            // it's already stopped: the change of the setting doesn't need to be applied
                            s_revertingVirtualDeviceEnabled = true;

                            try
                            {
                                NanoSettings.VirtualDeviceEnabled = false;
                            }
                            finally
                            {
                                s_revertingVirtualDeviceEnabled = false;
                            }
                        }
                    }
                    else if (!enable && service.VirtualDeviceIsRunning)
                    {
                        service.StopVirtualDevice();
                    }
                });
        }

        private static async Task RunVirtualDeviceOperationAsync(string progressText, Func<IVirtualDeviceService, Task> operation)
        {
            var service = s_virtualDeviceService;

            if (service is null)
            {
                return;
            }

            Interlocked.Increment(ref s_runningVirtualDeviceOperations);
            BeginOperation(progressText);

            try
            {
                await Task.Run(async delegate
                {
                    await s_virtualDeviceLock.WaitAsync();

                    try
                    {
                        // the virtual device can still be set up after Visual Studio started
                        while (service.IsOperationRunning)
                        {
                            await Task.Delay(500);
                        }

                        await operation(service);
                    }
                    catch (Exception ex)
                    {
                        // OK to catch all
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: {ex.Message}");

                        ExtensionTelemetry.TrackException(ex, "deviceexplorer.virtualdevice");
                    }
                    finally
                    {
                        s_virtualDeviceLock.Release();
                    }
                });
            }
            finally
            {
                // clear status bar
                MessageCentre.StopProgressMessage();

                Interlocked.Decrement(ref s_runningVirtualDeviceOperations);
                EndOperation();
            }
        }

        #endregion

        #region device details

        /// <summary>
        /// Gets the details of <paramref name="device"/> known without connecting to it: from the device enumeration and from the last capabilities query.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> GetDeviceDetails(NanoDeviceBase device)
        {
            var details = new List<KeyValuePair<string, string>>();

            if (device is null)
            {
                return details;
            }

            void Add(string name, string value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    details.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            try
            {
                Add("Device", device.Description);
                Add("Target", device.TargetName);
                Add("Platform", device.Platform);
                Add("Connection", $"{device.Transport} {device.ConnectionId}".Trim());
                Add("CLR version", KnownVersion(device.CLRVersion));

                if (device.DeviceInfo is { Valid: true } deviceInfo)
                {
                    Add("Firmware", KnownVersion(deviceInfo.SolutionBuildVersion));
                    Add("Firmware info", deviceInfo.SolutionBuildInfo);
                    Add("HAL", KnownVersion(deviceInfo.HalBuildVersion));
                    Add("Target framework", KnownVersion(deviceInfo.TargetFrameworkVersion));
                }

                Add("Booter", DescribeBooter(device));
                Add("nanoBooter version", device.HasNanoBooter ? KnownVersion(device.BooterVersion) : null);
                Add("Serial number", device.SerialNumber);
            }
            catch (Exception ex)
            {
                // the device can be gone while reading its properties
                MessageCentre.InternalErrorWriteLine($"Error reading details of {device.Description}: {ex.Message}");
            }

            return details;
        }

        /// <summary>
        /// The data read from <paramref name="device"/>, <see langword="null"/> if it wasn't read yet or it changed since.
        /// </summary>
        public static DeviceCapabilitiesData GetCachedCapabilitiesData(NanoDeviceBase device)
        {
            if (device is null)
            {
                return null;
            }

            lock (s_capabilitiesCache)
            {
                return s_capabilitiesCache.TryGetValue(device, out var data) ? data : null;
            }
        }

        /// <summary>
        /// The capabilities of <paramref name="device"/> as text, <see langword="null"/> if they weren't read yet or they changed since.
        /// </summary>
        public static string GetCachedCapabilities(NanoDeviceBase device) => GetCachedCapabilitiesData(device)?.Text;

        /// <summary>
        /// Gets the details of <paramref name="device"/> as text, to be copied into a bug report.
        /// </summary>
        public static string GetDeviceDetailsText(NanoDeviceBase device)
        {
            var text = new StringBuilder();

            foreach (var detail in GetDeviceDetails(device))
            {
                text.AppendLine($"{detail.Key}: {detail.Value}");
            }

            var capabilities = GetCachedCapabilities(device);

            if (!string.IsNullOrEmpty(capabilities))
            {
                text.AppendLine();
                text.AppendLine(capabilities.Trim());
            }

            return text.ToString();
        }

        // versions are 0.0 until the device is queried
        private static string KnownVersion(Version version) =>
            version is null || version.Major == 0 && version.Minor == 0 && version.Build <= 0 && version.Revision <= 0 ? null : version.ToString();

        private static string DescribeBooter(NanoDeviceBase device)
        {
            var booters = new List<string>();

            if (device.HasNanoBooter)
            {
                booters.Add("nanoBooter");
            }

            if (device.HasProprietaryBooter)
            {
                booters.Add("proprietary bootloader");
            }

            if (device.HasMCUboot)
            {
                booters.Add("MCUboot");
            }

            return booters.Count > 0 ? string.Join(", ", booters) : "none";
        }

        #endregion

        #region helpers

        private static async Task RunDeviceOperationAsync(
            string operationName,
            string progressMessageFormat,
            bool connect,
            Func<NanoDeviceBase, string, TelemetryOperation, Task> operation,
            bool quiet = false)
        {
            var device = ViewModel.SelectedDevice;

            if (device is null || !CanRunDeviceOperation)
            {
                return;
            }

            var description = device.Description;

            BeginOperation(string.Format(progressMessageFormat, description));

            await Task.Run(async delegate
            {
                using TelemetryOperation telemetry = ExtensionTelemetry.StartOperation(operationName);

                GlobalExclusiveDeviceAccess exclusiveAccess = null;
                try
                {
                    telemetry.SetProperties(ExtensionTelemetry.DeviceProperties(device));

                    // Get exclusive access to the device, but don't wait forever
                    exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(device, ExclusiveAccessTimeout);
                    if (exclusiveAccess is null)
                    {
                        if (!quiet)
                        {
                            Report($"Cannot access {description}, another application is using the device.", isWarning: true);
                        }

                        telemetry.Complete("NoExclusiveAccess");

                        return;
                    }

                    // check if debugger engine exists
                    if (device.DebugEngine == null)
                    {
                        device.CreateDebugEngine();
                    }

                    // connect to the device
                    if (connect && !device.DebugEngine.Connect())
                    {
                        if (!quiet)
                        {
                            ReportNotResponding(description, telemetry);
                        }

                        return;
                    }

                    await operation(device, description, telemetry);
                }
                catch (Exception ex)
                {
                    // OK to catch all
                    telemetry.Fail(ex, operationName);
                }
                finally
                {
                    // disconnect device
                    device.DebugEngine?.Stop(true);

                    exclusiveAccess?.Dispose();

                    // clear status bar
                    MessageCentre.StopProgressMessage();

                    EndOperation();
                }
            });
        }

        private static bool ReadCapabilities(NanoDeviceBase device, DeviceExplorerViewModel deviceExplorer)
        {
            try
            {
                // check that we are in CLR
                if (device.DebugEngine.IsConnectedTonanoCLR)
                {
                    // get device info
                    var deviceInfo = device.GetDeviceInfo(true);
                    var memoryMap = device.DebugEngine.MemoryMap;
                    var flashMap = device.DebugEngine.FlashSectorMap;
                    var deploymentMap = device.DebugEngine.GetDeploymentMap();

                    // we have to have a valid device info
                    if (!deviceInfo.Valid)
                    {
                        return false;
                    }

                    // load view model properties for maps
                    deviceExplorer.DeviceMemoryMap = new StringBuilder(memoryMap?.ToStringForOutput() ?? "Empty");
                    deviceExplorer.DeviceFlashSectorMap = new StringBuilder(flashMap?.ToStringForOutput() ?? "Empty");
                    deviceExplorer.DeviceDeploymentMap = new StringBuilder(deploymentMap?.ToStringForOutput() ?? "Empty");

                    // load view model property for system
                    deviceExplorer.DeviceSystemInfo = new StringBuilder(deviceInfo?.ToString() ?? "Empty");

                    // the same data, for the Properties window
                    StoreCapabilities(new DeviceCapabilitiesData(
                        device,
                        ConnectionSource.nanoCLR,
                        deviceInfo,
                        memoryMap,
                        flashMap,
                        deploymentMap,
                        device.DebugEngine.Capabilities,
                        null,
                        deviceInfo.ToString()));
                }
                else
                {
                    // we are in booter, can only get TargetInfo
                    var deviceInfo = device.DebugEngine?.TargetInfo;

                    // we have to have a valid device info
                    if (deviceInfo == null)
                    {
                        return false;
                    }

                    deviceExplorer.TargetInfo = new StringBuilder(deviceInfo.ToString() ?? "Empty");

                    StoreCapabilities(new DeviceCapabilitiesData(
                        device,
                        ConnectionSource.nanoBooter,
                        null,
                        null,
                        null,
                        null,
                        null,
                        deviceInfo.ToString(),
                        deviceInfo.ToString()));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // connects to the device and reads its capabilities into the view model and the cache
        private static bool ConnectAndReadCapabilities(NanoDeviceBase device, DeviceExplorerViewModel deviceExplorer)
        {
            // reset property to force that device capabilities are retrieved on next connection, until they are read
            deviceExplorer.LastDeviceConnectedHash = 0;

            if (!device.DebugEngine.Connect(false, true))
            {
                return false;
            }

            // keep device description hash code and the connection source, for the output of the capabilities
            deviceExplorer.LastDeviceConnectedHash = device.Description.GetHashCode();
            deviceExplorer.LastDeviceConnectionSource = device.DebugEngine.ConnectionSource;

            if (!ReadCapabilities(device, deviceExplorer))
            {
                deviceExplorer.LastDeviceConnectedHash = 0;

                return false;
            }

            return true;
        }

        private static void StoreCapabilities(DeviceCapabilitiesData data)
        {
            var available = ViewModel?.AvailableDevicesSnapshot;

            lock (s_capabilitiesCache)
            {
                // the devices that are gone are forgotten
                foreach (var device in s_capabilitiesCache.Keys.Where(d => available != null && !available.Contains(d)).ToList())
                {
                    s_capabilitiesCache.Remove(device);
                }

                s_capabilitiesCache[data.Device] = data;
            }
        }

        private static void OutputCapabilities(DeviceExplorerViewModel deviceExplorer)
        {
            if (deviceExplorer.LastDeviceConnectionSource == ConnectionSource.nanoCLR)
            {
                // CLR, output full details
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("System Information");
                MessageCentre.OutputMessage(deviceExplorer.DeviceSystemInfo.ToString());

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(deviceExplorer.DeviceMemoryMap.ToString());

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(deviceExplorer.DeviceFlashSectorMap.ToString());

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("Deployment Map");
                MessageCentre.OutputMessage(deviceExplorer.DeviceDeploymentMap.ToString());
                MessageCentre.OutputMessage(string.Empty);
            }
            else if (deviceExplorer.LastDeviceConnectionSource == ConnectionSource.nanoBooter)
            {
                // booter, can only output minimal details
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("Target Information");
                MessageCentre.OutputMessage(deviceExplorer.TargetInfo.ToString());
            }
        }

        /// <summary>
        /// Records which kind of configuration was saved. Only flags and the result, never addresses, SSIDs, passwords or certificates.
        /// </summary>
        private static void TrackNetworkConfigSaved(
            DeviceConfiguration.NetworkConfigurationProperties configuration,
            string failedStep,
            Engine.UpdateDeviceResult result,
            bool rootCaUploaded,
            bool deviceCertificateUploaded)
        {
            ExtensionTelemetry.TrackEvent("NetworkConfigSaved", new TelemetryProperties
            {
                { "dhcp", configuration.StartupAddressMode == AddressMode.DHCP },
                { "wireless", configuration.InterfaceType == NetworkInterfaceType.Wireless80211 },
                { "root_ca_uploaded", rootCaUploaded },
                { "device_cert_uploaded", deviceCertificateUploaded },
                { "failed_step", failedStep },
                { "result", result },
            });
        }

        private static void ReportNotResponding(string description, TelemetryOperation telemetry)
        {
            Report($"{description} is not responding, please reboot the device.", isWarning: true);

            telemetry.Complete("NotConnecting");
        }

        private static void Report(string message, bool isWarning = false)
        {
            MessageCentre.OutputMessage(message);
        }

        private static void BeginOperation(string text)
        {
            Interlocked.Increment(ref s_runningOperations);
            Volatile.Write(ref s_operationText, text);

            MessageCentre.StartProgressMessage(text);

            RaiseStateChanged();
        }

        private static void SetOperationText(string text)
        {
            Volatile.Write(ref s_operationText, text);

            RaiseStateChanged();
        }

        private static void EndOperation()
        {
            if (Interlocked.Decrement(ref s_runningOperations) == 0)
            {
                Volatile.Write(ref s_operationText, null);
            }

            RaiseStateChanged();
        }

        private static void SetEnumerating(bool value)
        {
            Volatile.Write(ref s_isEnumerating, value);

            RaiseStateChanged();
        }

        private static void OnSelectedDeviceChanged()
        {
            s_nanoDeviceCommService?.SelectDevice(ViewModel.SelectedDevice?.Description);

            RaiseStateChanged();
        }

        private static void OnDeviceEnumerationCompleted()
        {
            SetEnumerating(false);
        }

        private static void RaiseStateChanged() => StateChanged?.Invoke(null, EventArgs.Empty);

        #endregion
    }

    /// <summary>
    /// The data read from a device by the last capabilities query.
    /// </summary>
    internal sealed class DeviceCapabilitiesData
    {
        public DeviceCapabilitiesData(
            NanoDeviceBase device,
            ConnectionSource connectionSource,
            INanoFrameworkDeviceInfo deviceInfo,
            List<Commands.Monitor_MemoryMap.Range> memoryMap,
            List<Commands.Monitor_FlashSectorMap.FlashSectorData> flashSectorMap,
            List<Commands.Monitor_DeploymentMap.DeploymentData> deploymentMap,
            CLRCapabilities capabilities,
            string targetInfo,
            string text)
        {
            Device = device;
            ConnectionSource = connectionSource;
            DeviceInfo = deviceInfo;
            MemoryMap = memoryMap;
            FlashSectorMap = flashSectorMap;
            DeploymentMap = deploymentMap;
            Capabilities = capabilities;
            TargetInfo = targetInfo;
            Text = text;
        }

        public NanoDeviceBase Device { get; }

        public ConnectionSource ConnectionSource { get; }

        public INanoFrameworkDeviceInfo DeviceInfo { get; }

        public List<Commands.Monitor_MemoryMap.Range> MemoryMap { get; }

        public List<Commands.Monitor_FlashSectorMap.FlashSectorData> FlashSectorMap { get; }

        public List<Commands.Monitor_DeploymentMap.DeploymentData> DeploymentMap { get; }

        public CLRCapabilities Capabilities { get; }

        public string TargetInfo { get; }

        /// <summary>
        /// The capabilities as text, as they are output.
        /// </summary>
        public string Text { get; }
    }
}
