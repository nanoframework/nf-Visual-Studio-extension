//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight.Messaging;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension.FirmwareUpdate
{
    public class UpdateManager
    {
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

            Messenger.Default.Register<NotificationMessage>(s_instance, DeviceExplorerViewModel.MessagingTokens.LaunchFirmwareUpdateForNanoDevice, (message) => s_instance.LaunchUpdateAsync(message.Notification).ConfigureAwait(false));
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

        private async Task LaunchUpdateAsync(string deviceId)
        {
            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                // launch update, if enabled
                if (NanoFrameworkPackage.SettingAutoUpdateEnable)
                {
                    var nanoDevice = ViewModelLocator.DeviceExplorer.AvailableDevices.FirstOrDefault(d => d.DeviceId == Guid.Parse(deviceId));

                    // sanity check
                    if (
                        nanoDevice == null ||
                        nanoDevice.DebugEngine == null ||
                        nanoDevice.TargetName == null ||
                        nanoDevice.Platform == null)
                    {
                        // can't update this device
                        return;
                    }

#if !DEBUG
                    // check if this device has been updated ever or in the last hour
                    if (devicesUpdatED.TryGetValue(deviceId, out var updateTimeStamp))
                    {
                        // it's on the list, check if we had it updated in the last hour
                        if ((DateTime.UtcNow - ((DateTime)updateTimeStamp)).TotalHours < 1)
                        {
                            // no need to update
                            return;
                        }
                    }
#endif
                    // add this device to the updatING list
                    if (!devicesUpdatING.TryAdd(deviceId, new object()))
                    {
                        // fail to add device to list
                        // quit, never mind, this is not critical whatsoever
                        return;
                    }

                    // store for later use
                    var deviceDescription = nanoDevice.Description;

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
                            // need to create debug engine?
                            if (nanoDevice.DebugEngine == null)
                            {
                                nanoDevice.CreateDebugEngine();
                            }

                            // sanity check
                            if (nanoDevice.DebugEngine == null)
                            {
                                // quit 
                                return;
                            }

                            await Task.Yield();

                            if (await nanoDevice.DebugEngine.ConnectAsync(5000, true))
                            {
                                Version currentClrVersion = null;

                                // try to store CLR version
                                if(nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                                {
                                    if (nanoDevice.DeviceInfo.Valid)
                                    {
                                        currentClrVersion = nanoDevice.DeviceInfo.SolutionBuildVersion;
                                    }
                                }

                                // update conditions:
                                // 1. Running CLR _and_ the new version is higher
                                // 2. Running nanoBooter and there is no version information on the CLR (presumably because there is no CLR installed)
                                if (fwPackage.Version > nanoDevice.ClrVersion)
                                {
                                    bool attemptToLaunchBooter = false;

                                    // any update has to be handled by nanoBooter, so let's have it running
                                    try
                                    {
                                        MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Launching nanoBooter...");

                                        attemptToLaunchBooter = await nanoDevice.ConnectToNanoBooterAsync(CancellationToken.None);

                                        // check for version where the software reboot to nanoBooter was made available
                                        if (!attemptToLaunchBooter &&
                                            (currentClrVersion != null &&
                                            nanoDevice.DeviceInfo.SolutionBuildVersion < new Version("1.6.0.54")))
                                        {
                                            MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] The device is running a version that doesn't support rebooting by software. Please update your device using 'nanoff' tool.");
                                        }
                                    }
                                    catch
                                    {
                                        // this reboot step can go wrong and there's no big deal with that
                                    }

                                    await Task.Yield();

                                    // check if the device is still there
                                    if(ViewModelLocator.DeviceExplorer.AvailableDevices.FirstOrDefault(d => d.DeviceId == Guid.Parse(deviceId)) == null)
                                    {
                                        return;
                                    }

                                    if (attemptToLaunchBooter &&
                                        nanoDevice.Ping() == PingConnectionType.nanoBooter)
                                    {
                                        // get address for CLR block expected by device
                                        var clrAddress = nanoDevice.GetClrStartAddress();

                                        // compare with address on the fw packages
                                        if (clrAddress !=
                                            (fwPackage as Stm32Firmware).ClrStartAddress)
                                        {
                                            // CLR addresses don't match, can't proceed with update
                                            MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] ERROR: Can't update device. CLR addresses are different. Please update nanoBooter manually.");
                                            return;
                                        }

                                        // update black list to include this device

                                        // need to get device port
                                        string devicePort = "";

                                        NanoDevice<NanoSerialDevice> serialDevice = nanoDevice as NanoDevice<NanoSerialDevice>;

                                        if (serialDevice != null)
                                        {
                                            devicePort = serialDevice.ConnectionId;
                                        }

                                        NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortBlackList.Add(devicePort);

                                        await Task.Yield();

                                        MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Starting update to CLR v{fwPackage.Version}.");

                                        try
                                        {
                                            await Task.Yield();

                                            // create a progress indicator to be used by deployment operation to post debug messages
                                            var progressIndicator = new Progress<string>(m => MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] {m}"));

                                            if (await nanoDevice.DeployBinaryFileAsync(
                                                (fwPackage as Stm32Firmware).nanoClrFileBin,
                                                (fwPackage as Stm32Firmware).ClrStartAddress,
                                                CancellationToken.None,
                                                progressIndicator))
                                            {
                                                await Task.Yield();

                                                MessageCentre.OutputFirmwareUpdateMessage($"[{deviceDescription}] Update successful.");

                                                // need to remove it from black list before it reboots
                                                NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortBlackList.Remove(devicePort);

                                                // add it to the list of devices updatED with the update time stamp
                                                devicesUpdatED.TryAdd(deviceId, DateTime.UtcNow);

                                                // try to reboot target 

                                                await Task.Yield();

                                                // check if the device is still there
                                                if (ViewModelLocator.DeviceExplorer.AvailableDevices.FirstOrDefault(d => d.DeviceId == Guid.Parse(deviceId)) == null)
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
                                        finally
                                        {
                                            // this has to be here in case there is an exception during deployment
                                            NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortBlackList.Remove(devicePort);
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
                                        devicesUpdatED.TryAdd(deviceId, DateTime.UtcNow);
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
                            devicesUpdatED.TryAdd(deviceId, DateTime.UtcNow);
                        }
                        ///////////////////////////////////////
                        // TI CC13x26x2
                        else if (fwPackage is CC13x26x2Firmware)
                        {
                            // TODO
                            // not supported yet

                            MessageCentre.OutputFirmwareUpdateMessage("The ability to update CC13x26x2 targets is not currently available. Yet...");

                            // add it to the list of devices updatED with the update time stamp
                            devicesUpdatED.TryAdd(deviceId, DateTime.UtcNow);
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
                    !NanoFrameworkPackage.SettingAllowPreviewUpdates);
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
                    !NanoFrameworkPackage.SettingAllowPreviewUpdates);

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
