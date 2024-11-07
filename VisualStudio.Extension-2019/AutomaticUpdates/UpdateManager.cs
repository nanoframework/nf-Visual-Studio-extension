// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Messaging;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.NFDevice;
using nanoFramework.Tools.VisualStudio.Extension.FirmwareUpdate;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension.AutomaticUpdates
{
    public class UpdateManager
    {
        private const int ExclusiveAccessTimeout = 3000;
        private static UpdateManager s_instance;
        private ViewModelLocator ViewModelLocator;
        private readonly Package _package;

        private readonly ConcurrentDictionary<string, object> devicesUpdatING = new ConcurrentDictionary<string, object>();
        private readonly ConcurrentDictionary<string, object> devicesUpdatED = new ConcurrentDictionary<string, object>();

        private UpdateManager(Package package)
        {
            _package = package ?? throw new ArgumentNullException($"{package} can't be null.");
        }

        public static void Initialize(
            AsyncPackage package,
            ViewModelLocator vmLocator)
        {
            s_instance = new UpdateManager(package)
            {
                ViewModelLocator = vmLocator
            };

            Messenger.Default.Register<NotificationMessage>(s_instance, DeviceExplorerViewModel.MessagingTokens.LaunchFirmwareUpdateForNanoDevice, (message) => s_instance.LaunchUpdate(message.Notification));
            Messenger.Default.Register<NotificationMessage>(s_instance, DeviceExplorerViewModel.MessagingTokens.NanoDeviceHasDeparted, (message) => s_instance.ProcessNanoDeviceDeparture(message.Notification));
        }

        private void ProcessNanoDeviceDeparture(string deviceId)
        {
            try
            {
                // device has departed, try remove it from list, in case it's there
                devicesUpdatING.TryRemove(deviceId, out var dummy);
            }
            catch
            {
                // no need to process any failure on the attempt to remove the device from the list
            }
        }

        private void LaunchUpdate(string deviceId)
        {
            _ = Task.Run(async delegate
            {
                // launch update, if enabled
                if (NanoFrameworkPackage.SettingAutoUpdateEnable)
                {
                    await Task.Delay(500);

                    await Task.Yield();

                    var deviceUniqueId = Guid.Parse(deviceId);

                    var nanoDevice = ViewModelLocator.DeviceExplorer.AvailableDevices.FirstOrDefault(d => d.DeviceUniqueId == deviceUniqueId);

                    // sanity check
                    if (
                        nanoDevice == null ||
                        nanoDevice.TargetName == null ||
                        nanoDevice.Platform == null)
                    {
                        // can't update this device

#if DEBUG
                        Console.WriteLine($"[Automatic Updates] Dropping {nanoDevice?.TargetName} on wrong conditions");
#endif
                        return;
                    }

                    // store for later use
                    var deviceDescription = nanoDevice.Description;

#if !DEBUG
                    // check if this device has been updated ever or in the last hour
                    if (devicesUpdatED.TryGetValue(deviceDescription, out var updateTimeStamp))
                    {
                        // it's on the list, check if we had it updated in the last hour
                        if ((DateTime.UtcNow - ((DateTime)updateTimeStamp)).TotalMinutes < 60)
                        {
                            // no need to update
                            return;
                        }
                    }
#endif
                    GlobalExclusiveDeviceAccess exclusiveAccess = null;
                    try
                    {
                        // Get exclusive access to the device, but don't wait forever
                        exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(nanoDevice, ExclusiveAccessTimeout);
                        if (exclusiveAccess is null)
                        {
                            // Can't get access, quit update for now
#if DEBUG
                            Console.WriteLine($"[Automatic Updates] Cannot access {nanoDevice.Description}, another application is using the device");
#endif
                            return;
                        }


                        // check if DebugEngine is available
                        if (nanoDevice.DebugEngine == null)
                        {
                            nanoDevice.CreateDebugEngine();
                        }

                        if (nanoDevice.DebugEngine == null)
                        {
                            // can't create it, quit update now
                            return;
                        }

                        // add this device to the updatING list
                        if (!devicesUpdatING.TryAdd(deviceId, new object()))
                        {
                            // fail to add device to list
#if DEBUG
                            Console.WriteLine($"[Automatic Updates] {nanoDevice.TargetName} update already in progress.");
#endif

                            // quit, never mind, this is not critical whatsoever
                            return;
                        }

                        // better wrap this on a try-finally because a lot of things can go wrong in the process
                        try
                        {
                            await Task.Yield();

                            var fwPackage = await GetFirmwarePackageAsync(
                                nanoDevice.TargetName,
                                nanoDevice.Platform);

                            await Task.Yield();

                            //////////////////////////////
                            // STM32 targets
                            if (fwPackage is Stm32Firmware)
                            {
                                // sanity check
                                if (nanoDevice.DebugEngine == null)
                                {
#if DEBUG
                                    Console.WriteLine($"[Automatic Updates] {nanoDevice.TargetName} debug engine is not ready.");
#endif
                                    // quit 
                                    return;
                                }

                                if (nanoDevice.DebugEngine.Connect(
                                    1000,
                                    true,
                                    true))
                                {
                                    Version currentClrVersion = null;

                                    // try to store CLR version
                                    if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                                    {
                                        if (nanoDevice.DeviceInfo.Valid)
                                        {
                                            currentClrVersion = nanoDevice.DeviceInfo.SolutionBuildVersion;
                                        }
                                    }

                                    // update conditions:
                                    // 1. Running CLR _and_ the new version is higher
                                    // 2. Running nanoBooter and there is no version information on the CLR (presumably because there is no CLR installed)
                                    if (fwPackage.Version > nanoDevice.CLRVersion)
                                    {
                                        bool attemptToLaunchBooter = false;

                                        if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                                        {
                                            // any update has to be handled by nanoBooter, so let's have it running
                                            try
                                            {
                                                MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Launching nanoBooter...");

                                                attemptToLaunchBooter = nanoDevice.ConnectToNanoBooter();

                                                if (!attemptToLaunchBooter)
                                                {
                                                    // check for version where the software reboot to nanoBooter was made available
                                                    if (currentClrVersion != null &&
                                                        nanoDevice.DeviceInfo.SolutionBuildVersion < new Version("1.6.0.54"))
                                                    {
                                                        MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] The device is running a version that doesn't support rebooting by software. Please update your device using 'nanoff' tool.");

                                                        await Task.Yield();
                                                    }
                                                }
                                            }
                                            catch
                                            {
                                                // this reboot step can go wrong and there's no big deal with that
                                            }
                                        }
                                        else
                                        {
                                            attemptToLaunchBooter = true;
                                        }

                                        // check if the device is still there
                                        if (ViewModelLocator.DeviceExplorer.AvailableDevices.FirstOrDefault(d => d.DeviceUniqueId == deviceUniqueId) == null)
                                        {
#if DEBUG
                                            Console.WriteLine($"[Automatic Updates] {nanoDevice.TargetName} is not available anymore.");
#endif
                                            return;
                                        }

                                        if (attemptToLaunchBooter &&
                                            nanoDevice.Ping() == Debugger.WireProtocol.ConnectionSource.nanoBooter)
                                        {
                                            // get address for CLR block expected by device
                                            var clrAddress = nanoDevice.GetCLRStartAddress();

                                            // compare with address on the fw packages
                                            if (clrAddress !=
                                                (fwPackage as Stm32Firmware).ClrStartAddress)
                                            {
                                                // CLR addresses don't match, can't proceed with update
                                                MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] ERROR: Can't update device. CLR addresses are different. Please update nanoBooter manually.");
                                                return;
                                            }

                                            await Task.Yield();

                                            MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Starting update to CLR v{fwPackage.Version}.");

                                            try
                                            {
                                                await Task.Yield();

                                                // create a progress indicator to be used by deployment operation to post debug messages
                                                var progressIndicator = new Progress<string>(m => MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] {m}"));

                                                if (nanoDevice.DeployBinaryFile(
                                                    (fwPackage as Stm32Firmware).nanoClrFileBin,
                                                    (fwPackage as Stm32Firmware).ClrStartAddress,
                                                    progressIndicator))
                                                {
                                                    await Task.Yield();

                                                    MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Update successful.");

                                                    // add it to the list of devices updatED with the update time stamp
                                                    devicesUpdatED.TryAdd(deviceDescription, DateTime.UtcNow);
                                                }

                                                // if this is the selected device...
                                                if (ViewModelLocator.DeviceExplorer.SelectedDevice?.DeviceUniqueId == deviceUniqueId)
                                                {
                                                    // ...reset property to force that device capabilities to be retrieved on next connection
                                                    ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;
                                                }

                                                if (attemptToLaunchBooter)
                                                {
                                                    // try to reboot target 

                                                    // remove it from updatING list
                                                    devicesUpdatING.TryRemove(deviceId, out var dummy);

                                                    // check if the device is still there
                                                    if (ViewModelLocator.DeviceExplorer.AvailableDevices.FirstOrDefault(d => d.DeviceUniqueId == Guid.Parse(deviceId)) == null)
                                                    {
                                                        return;
                                                    }

                                                    MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Rebooting...");

                                                    nanoDevice.DebugEngine.RebootDevice(RebootOptions.NormalReboot);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] ERROR: Exception occurred when performing update ({ex.Message}).");
                                            }
                                        }
                                        else
                                        {
                                            if (attemptToLaunchBooter)
                                            {
                                                // only report this as an error if the launch was successful
                                                MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] ERROR: Failed to launch nanoBooter. Quitting update.");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // just to make sure that the CLR version is the latest, so we don't check it over and over
                                        if (nanoDevice.DebugEngine.IsConnectedTonanoCLR &&
                                            (fwPackage.Version == nanoDevice.DeviceInfo.ClrBuildVersion))
                                        {
                                            // add it to the list of devices updatED with the update time stamp
                                            devicesUpdatED.TryAdd(deviceDescription, DateTime.UtcNow);
                                        }
                                    }
                                }
                                else
                                {
                                    MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] ERROR: Can't connect to device. Quitting update.");
                                }
                            }
                            ///////////////////////////////////
                            // ESP32 targets
                            else if (fwPackage is Esp32Firmware)
                            {
                                // TODO
                                // not supported yet

                                MessageCentre.OutputFirmwareUpdateMessage("The ability to update ESP32 targets is not currently available. Yet...");

                                // add it to the list of devices updatED with the update time stamp
                                devicesUpdatED.TryAdd(deviceDescription, DateTime.UtcNow);
                            }
                            ///////////////////////////////////////
                            // TI CC13x26x2
                            else if (fwPackage is CC13x26x2Firmware)
                            {
                                // TODO
                                // not supported yet

                                MessageCentre.OutputFirmwareUpdateMessage("The ability to update CC13x26x2 targets is not currently available. Yet...");

                                // add it to the list of devices updatED with the update time stamp
                                devicesUpdatED.TryAdd(deviceDescription, DateTime.UtcNow);
                            }
                            else
                            {
                                // shouldn't be here....
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] ERROR: Exception occurred when performing update ({ex.Message}).");
                        }
                        finally
                        {
                            // remove it from updatING list
                            devicesUpdatING.TryRemove(deviceId, out var dummy);
                        }

                    }
                    finally
                    {
                        nanoDevice.DebugEngine?.Stop();

                        exclusiveAccess?.Dispose();
                    }
                }
            });
        }

        internal static async Task<FirmwarePackage> GetFirmwarePackageAsync(
            string targetName,
            string platformName)
        {
            if (platformName.StartsWith("STM32"))
            {
                var fwPackage = new Stm32Firmware(
                    targetName,
                    "",
                    !NanoFrameworkPackage.SettingIncludePrereleaseUpdates);
                if (await fwPackage.DownloadAndExtractAsync())
                {
                    return fwPackage;
                }
            }
            else if (platformName.StartsWith("ESP32"))
            {
                var fwPackage = new Esp32Firmware(
                    targetName,
                    "",
                    !NanoFrameworkPackage.SettingIncludePrereleaseUpdates);

                // TODO using this flash size for the moment, will have to figure a way to have this declared in the device caps
                if (await fwPackage.DownloadAndExtractAsync(0x400000))
                {
                    return fwPackage;
                }
            }

            return null;
        }
    }
}
