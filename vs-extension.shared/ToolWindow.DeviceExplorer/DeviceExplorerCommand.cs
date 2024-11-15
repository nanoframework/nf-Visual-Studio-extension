// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.Design;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
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
    /// Command handler
    /// </summary>
    internal sealed class DeviceExplorerCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        /////////////////////////////////////////////
        // this GUID is coming from the .vsct file //
        /////////////////////////////////////////////
#if DEV17
        public static readonly Guid CommandSet = new Guid("63a515b6-b822-40f8-942e-ee3f8290a1c0");
#elif DEV16
        public static readonly Guid CommandSet = new Guid("c975c4ec-f229-45dd-b681-e42815641675");
#else
#error "Missing dev version constant. Maybe the platform is not correctly set? (x64 for VS2022 and AnyCPU for VS2019)."
#endif

        //private ViewModelLocator ViewModelLocator;

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        // command set Guids
        /////////////////////////////////////////////
        // this GUID is coming from the .vsct file //
        /////////////////////////////////////////////
#if DEV17
        public const string guidDeviceExplorerCmdSet = "62f05d80-2da6-4de1-ba71-c4c37369b8c7";
#elif DEV16
        public const string guidDeviceExplorerCmdSet = "DF641D51-1E8C-48E4-B549-CC6BCA9BDE19";
#else
#error "Missing dev version constant. Maybe the platform is not correctly set? (x64 for VS2022 and AnyCPU for VS2019)."
#endif

        public const int DeviceExplorerToolbarID = 0x1000;

        // toolbar commands

        // 1st group
        public const int PingDeviceCommandID = 0x0210;
        public const int DeviceCapabilitiesID = 0x0220;
        public const int DeviceEraseID = 0x0230;
        public const int RebooMenuGroupID = 0x0240;
        public const int RebootClrID = 0x0242;
        public const int RebootNanoBooterID = 0x0244;
        public const int RebootBootloaderID = 0x0246;
        public const int NetworkConfigID = 0x0250;

        // 2nd group
        public const int DisableDeviceWatchersCommandID = 0x0400;
        public const int RescanDevicesCommandID = 0x0410;

        // 3r group
        public const int ShowInternalErrorsCommandID = 0x0300;
        public const int ShowSettingsCommandID = 0x0420;

        // Timeout for exclusive access
        private const int ExclusiveAccessTimeout = 3000;

        INanoDeviceCommService NanoDeviceCommService;
        OleMenuCommandService MenuCommandService;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceExplorerCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private DeviceExplorerCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException("Package can't be null.");
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            MenuCommandService = commandService;

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(ShowToolWindow, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static DeviceExplorerCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in ToolWindow1Command's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;

            Instance = new DeviceExplorerCommand(package, commandService);

            var commService = await package.GetServiceAsync(typeof(NanoDeviceCommService));

            Instance.NanoDeviceCommService = commService as INanoDeviceCommService;

            await Instance.CreateToolbarHandlersAsync();

            Ioc.Default.GetService<DeviceExplorerViewModel>().NanoDeviceCommService = Instance.NanoDeviceCommService;

            // setup message listeners to be notified of events occurring in the View Model
            WeakReferenceMessenger.Default.Register<SelectedNanoDeviceHasChangedMessage>(Instance, static (r, message) => Instance.SelectedNanoDeviceHasChangedHandlerAsync().ConfigureAwait(false));
            WeakReferenceMessenger.Default.Register<NanoDevicesCollectionHasChangedMessage>(Instance, static (r, message) => Instance.NanoDevicesCollectionChangedHandlerAsync().ConfigureAwait(false));
            WeakReferenceMessenger.Default.Register<NanoDeviceEnumerationCompletedMessage>(Instance, static (r, message) => Instance.NanoDevicesDeviceEnumerationCompletedHandlerAsync().ConfigureAwait(false));
        }

        private async Task CreateToolbarHandlersAsync()
        {
            // Create the handles for the toolbar commands
            var menuCommandService = await ServiceProvider.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService ?? throw new ArgumentNullException(nameof(IMenuCommandService));

            CommandID toolbarButtonCommandId;
            MenuCommand menuItem;

            // PingCommand
            toolbarButtonCommandId = GenerateCommandID(PingDeviceCommandID);
            menuItem = new MenuCommand(new EventHandler(
                PingDeviceCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // DeviceCapabilities
            toolbarButtonCommandId = GenerateCommandID(DeviceCapabilitiesID);
            menuItem = new MenuCommand(new EventHandler(
                DeviceCapabilitiesCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // DeviceErase
            toolbarButtonCommandId = GenerateCommandID(DeviceEraseID);
            menuItem = new MenuCommand(new EventHandler(
                DeviceEraseCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // Reboot Menu Group
            toolbarButtonCommandId = GenerateCommandID(RebooMenuGroupID);
            menuItem = new MenuCommand(new EventHandler(
                RebootCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // Reboot CLR
            toolbarButtonCommandId = GenerateCommandID(RebootClrID);
            menuItem = new MenuCommand(new EventHandler(
                RebootCommandHandler), toolbarButtonCommandId);
            menuCommandService.AddCommand(menuItem);

            // Reboot nanoBooter
            toolbarButtonCommandId = GenerateCommandID(RebootNanoBooterID);
            menuItem = new MenuCommand(new EventHandler(
                RebootCommandHandler), toolbarButtonCommandId);
            menuCommandService.AddCommand(menuItem);

            // Reboot bootloader
            toolbarButtonCommandId = GenerateCommandID(RebootBootloaderID);
            menuItem = new MenuCommand(new EventHandler(
                RebootCommandHandler), toolbarButtonCommandId);
            menuCommandService.AddCommand(menuItem);

            // NetworkConfig
            toolbarButtonCommandId = GenerateCommandID(NetworkConfigID);
            menuItem = new MenuCommand(new EventHandler(
                NetworkConfigCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // Show Internal Errors
            toolbarButtonCommandId = GenerateCommandID(ShowInternalErrorsCommandID);
            menuItem = new MenuCommand(new EventHandler(
                ShowInternalErrorsCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = true;
            menuItem.Visible = true;
            // can't set the checked status here because the service provider of the preferences persistence is not available at this time
            // deferring to when the Device Explorer control is loaded
            menuCommandService.AddCommand(menuItem);

            // Disable Device Watchers
            toolbarButtonCommandId = GenerateCommandID(DisableDeviceWatchersCommandID);
            menuItem = new MenuCommand(new EventHandler(
                DisableDeviceWatchersHandler), toolbarButtonCommandId);
            menuItem.Enabled = true;
            menuItem.Visible = true;
            // can't set the checked status here because the service provider of the preferences persistence is not available at this time
            // deferring to when the Device Explorer control is loaded
            menuCommandService.AddCommand(menuItem);

            // Rescan Devices
            toolbarButtonCommandId = GenerateCommandID(RescanDevicesCommandID);
            menuItem = new MenuCommand(new EventHandler(
                RescanDevicesCommandHandler), toolbarButtonCommandId);
            // making it disabled for now, it will be updated according to DisableDeviceWatchers status when appropriate
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // Show Settings
            toolbarButtonCommandId = GenerateCommandID(ShowSettingsCommandID);
            menuItem = new MenuCommand(new EventHandler(
                ShowSettingsCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = true;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Shows the tool window when the menu item is clicked.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            _ = package.JoinableTaskFactory.RunAsync(async delegate
                {

                    // Get the instance number 0 of this tool window. This window is single instance so this instance
                    // is actually the only one.
                    // The last flag is set to true so that if the tool window does not exists it will be created.
                    ToolWindowPane toolWindow = await package.ShowToolWindowAsync(typeof(DeviceExplorer), 0, true, package.DisposalToken);
                    if ((null == toolWindow) || (null == toolWindow.Frame))
                    {
                        throw new NotSupportedException("Cannot create nanoFramework Device Explorer tool window.");
                    }

                    //IVsWindowFrame windowFrame = (IVsWindowFrame)toolWindow.Frame;
                    //ErrorHandler.ThrowOnFailure(windowFrame.Show());
                });
        }

        #region Command button handlers

        /// <summary>
        /// Handler for PingDeviceCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void PingDeviceCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            await Task.Run(async delegate
            {
                var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

                var descriptionBackup = deviceExplorer.SelectedDevice.Description;

                MessageCentre.StartProgressMessage($"Pinging {descriptionBackup}...");
                GlobalExclusiveDeviceAccess exclusiveAccess = null;
                try
                {
                    // disable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(false);

                    // make sure this device is showing as selected in Device Explorer tree view
                    deviceExplorer.ForceNanoDeviceSelection();

                    // Get exclusive access to the device, but don't wait forever
                    exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(deviceExplorer.SelectedDevice, ExclusiveAccessTimeout);
                    if (exclusiveAccess is null)
                    {
                        MessageCentre.OutputMessage($"Cannot access {descriptionBackup}, another application is using the device.");
                        return;
                    }

                    // check if debugger engine exists
                    if (NanoDeviceCommService.Device.DebugEngine == null)
                    {
                        NanoDeviceCommService.Device.CreateDebugEngine();
                    }

                    // connect to the device
                    if (!NanoDeviceCommService.Device.DebugEngine.Connect())
                    {
                        MessageCentre.OutputMessage($"{descriptionBackup} is not responding, please reboot the device.");

                        return;
                    }

                    // ping device
                    var reply = NanoDeviceCommService.Device.Ping();

                    if (reply == ConnectionSource.nanoBooter)
                    {
                        MessageCentre.OutputMessage($"{descriptionBackup} is active running nanoBooter.");
                    }
                    else if (reply == ConnectionSource.nanoCLR)
                    {
                        MessageCentre.OutputMessage($"{descriptionBackup} is active running nanoCLR.");
                    }
                    else
                    {
                        MessageCentre.OutputMessage($"No reply from {descriptionBackup}");
                    }
                }
                catch
                {
                    // OK to catch all
                }
                finally
                {
                    // disconnect device
                    NanoDeviceCommService.Device?.DebugEngine?.Stop(true);

                    exclusiveAccess?.Dispose();

                    // enable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(true);

                    MessageCentre.StopProgressMessage();
                }
            });
        }

        /// <summary>
        /// Handler for DeviceCapabilitiesCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void DeviceCapabilitiesCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

            var descriptionBackup = deviceExplorer.SelectedDevice.Description;

            MessageCentre.StartProgressMessage($"Querying {descriptionBackup} capabilities...");

            await Task.Run(async delegate
            {
                GlobalExclusiveDeviceAccess exclusiveAccess = null;
                try
                {
                    // disable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(false);

                    // make sure this device is showing as selected in Device Explorer tree view
                    deviceExplorer.ForceNanoDeviceSelection();

                    // only query device if it's different 
                    if (descriptionBackup.GetHashCode() != deviceExplorer.LastDeviceConnectedHash)
                    {
                        // Get exclusive access to the device, but don't wait forever
                        exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(deviceExplorer.SelectedDevice, ExclusiveAccessTimeout);
                        if (exclusiveAccess is null)
                        {
                            MessageCentre.OutputMessage($"Cannot access {descriptionBackup}, another application is using the device.");
                            return;
                        }

                        // check if debugger engine exists
                        if (NanoDeviceCommService.Device.DebugEngine == null)
                        {
                            NanoDeviceCommService.Device.CreateDebugEngine();
                        }

                        // connect to the device
                        if (NanoDeviceCommService.Device.DebugEngine.Connect(
                            false,
                            true))
                        {
                            // keep device description hash code to avoid get info twice
                            deviceExplorer.LastDeviceConnectedHash = descriptionBackup.GetHashCode();
                            // also store connection source
                            deviceExplorer.LastDeviceConnectionSource = NanoDeviceCommService.Device.DebugEngine.ConnectionSource;

                            // check that we are in CLR
                            if (NanoDeviceCommService.Device.DebugEngine.IsConnectedTonanoCLR)
                            {
                                try
                                {
                                    // get device info
                                    var deviceInfo = NanoDeviceCommService.Device.GetDeviceInfo(true);
                                    var memoryMap = NanoDeviceCommService.Device.DebugEngine.MemoryMap;
                                    var flashMap = NanoDeviceCommService.Device.DebugEngine.FlashSectorMap;
                                    var deploymentMap = NanoDeviceCommService.Device.DebugEngine.GetDeploymentMap();

                                    // we have to have a valid device info
                                    if (deviceInfo.Valid)
                                    {
                                        // load view model properties for maps
                                        deviceExplorer.DeviceMemoryMap = new StringBuilder(memoryMap?.ToStringForOutput() ?? "Empty");
                                        deviceExplorer.DeviceFlashSectorMap = new StringBuilder(flashMap?.ToStringForOutput() ?? "Empty");
                                        deviceExplorer.DeviceDeploymentMap = new StringBuilder(deploymentMap?.ToStringForOutput() ?? "Empty");

                                        // load view model property for system
                                        deviceExplorer.DeviceSystemInfo = new StringBuilder(deviceInfo?.ToString() ?? "Empty");
                                    }
                                    else
                                    {
                                        // reset property to force that device capabilities are retrieved on next connection
                                        deviceExplorer.LastDeviceConnectedHash = 0;

                                        // report issue to user
                                        MessageCentre.OutputMessage($"Error retrieving device information from {descriptionBackup}. Please reconnect device.");

                                        return;
                                    }
                                }
                                catch
                                {
                                    // reset property to force that device capabilities are retrieved on next connection
                                    deviceExplorer.LastDeviceConnectedHash = 0;

                                    // report issue to user
                                    MessageCentre.OutputMessage($"Error retrieving device information from {descriptionBackup}. Please reconnect device.");

                                    return;
                                }
                            }
                            else
                            {
                                // we are in booter, can only get TargetInfo
                                try
                                {
                                    // get device info
                                    var deviceInfo = NanoDeviceCommService.Device.DebugEngine?.TargetInfo;

                                    // we have to have a valid device info
                                    if (deviceInfo != null)
                                    {
                                        // load view model properties for maps
                                        deviceExplorer.TargetInfo = new StringBuilder(deviceInfo.ToString() ?? "Empty");
                                    }
                                    else
                                    {
                                        // reset property to force that device capabilities are retrieved on next connection
                                        deviceExplorer.LastDeviceConnectedHash = 0;

                                        // report issue to user
                                        MessageCentre.OutputMessage($"Error retrieving device information from {descriptionBackup}. Please reconnect device.");

                                        return;
                                    }
                                }
                                catch
                                {
                                    // reset property to force that device capabilities are retrieved on next connection
                                    deviceExplorer.LastDeviceConnectedHash = 0;

                                    // report issue to user
                                    MessageCentre.OutputMessage($"Error retrieving device information from {descriptionBackup}. Please reconnect device.");

                                    return;
                                }
                            }
                        }
                        else
                        {
                            // reset property to force that device capabilities are retrieved on next connection
                            deviceExplorer.LastDeviceConnectedHash = 0;

                            MessageCentre.OutputMessage($"{descriptionBackup} is not responding, please reboot the device.");

                            return;
                        }
                    }
                    else
                    {

                    }

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
                    else
                    {
                        // shouldn't get here...
                    }
                }
                catch
                {
                    // OK to catch all
                }
                finally
                {
                    // disconnect device
                    NanoDeviceCommService.Device?.DebugEngine?.Stop(true);

                    exclusiveAccess?.Dispose();

                    // enable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(true);

                    // clear status bar
                    MessageCentre.StopProgressMessage();
                }
            });
        }

        /// <summary>
        /// Handler for DeviceEraseCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void DeviceEraseCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

            var descriptionBackup = deviceExplorer.SelectedDevice.Description;

            var logProgressIndicator = new Progress<string>(MessageCentre.InternalErrorWriteLine);
            var progressIndicator = new Progress<MessageWithProgress>((m) => MessageCentre.StartMessageWithProgress(m));

            MessageCentre.StartProgressMessage($"Erasing {descriptionBackup} deployment area...");

            await Task.Run(async delegate
            {
                GlobalExclusiveDeviceAccess exclusiveAccess = null;
                try
                {
                    // disable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(false);

                    // make sure this device is showing as selected in Device Explorer tree view
                    deviceExplorer.ForceNanoDeviceSelection();

                    // Get exclusive access to the device, but don't wait forever
                    exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(deviceExplorer.SelectedDevice, ExclusiveAccessTimeout);
                    if (exclusiveAccess is null)
                    {
                        MessageCentre.OutputMessage($"Cannot access {descriptionBackup}, another application is using the device.");
                        return;
                    }

                    // check if debugger engine exists
                    if (NanoDeviceCommService.Device.DebugEngine == null)
                    {
                        NanoDeviceCommService.Device.CreateDebugEngine();
                    }

                    // connect to the device
                    if (NanoDeviceCommService.Device.DebugEngine.Connect(false, true))
                    {
                        try
                        {
                            if (NanoDeviceCommService.Device.Erase(
                                EraseOptions.Deployment,
                                progressIndicator,
                                logProgressIndicator))
                            {
                                MessageCentre.OutputMessage($"{descriptionBackup} deployment area erased.");

                                // reset the hash for the connected device so the deployment information can be refreshed, if and when requested
                                deviceExplorer.LastDeviceConnectedHash = 0;

                                // yield to give the UI thread a chance to respond to user input
                                await Task.Yield();
                            }
                            else
                            {
                                // report issue to user
                                MessageCentre.OutputMessage($"Error erasing {descriptionBackup} deployment area.");
                            }
                        }
                        catch
                        {
                            // report issue to user
                            MessageCentre.OutputMessage($"Error erasing {descriptionBackup} deployment area.");

                            return;
                        }
                    }
                    else
                    {
                        // reset property to force that device capabilities are retrieved on next connection
                        deviceExplorer.LastDeviceConnectedHash = 0;

                        MessageCentre.OutputMessage($"{descriptionBackup} is not responding, please reboot the device.");

                        return;
                    }
                }
                catch
                {
                    // OK to catch all
                }
                finally
                {
                    // disconnect device
                    NanoDeviceCommService.Device?.DebugEngine?.Stop(true);

                    exclusiveAccess?.Dispose();

                    // enable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(true);

                    // clear status bar
                    MessageCentre.StopProgressMessage();
                }
            });
        }

        /// <summary>
        /// Handler for NetworkConfigCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void NetworkConfigCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

            var descriptionBackup = deviceExplorer.SelectedDevice.Description;

            await Task.Run(async delegate
            {
                GlobalExclusiveDeviceAccess exclusiveAccess = null;
                try
                {
                    // disable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(false);

                    // make sure this device is showing as selected in Device Explorer tree view
                    deviceExplorer.ForceNanoDeviceSelection();

                    // Get exclusive access to the device, but don't wait forever
                    exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(deviceExplorer.SelectedDevice, ExclusiveAccessTimeout);
                    if (exclusiveAccess is null)
                    {
                        _ = MessageBox.Show($"Cannot access {deviceExplorer.SelectedDevice.Description}, another application is using the device.",
                                            ".NET nanoFramework Device Explorer",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Error);
                        return;
                    }

                    // check if debugger engine exists
                    if (NanoDeviceCommService.Device.DebugEngine == null)
                    {
                        NanoDeviceCommService.Device.CreateDebugEngine();
                    }

                    // connect to the device
                    if (NanoDeviceCommService.Device.DebugEngine.Connect(false, true))
                    {
                        try
                        {
                            // for now, just get the 1st network configuration, if exists
                            var networkConfigurations = NanoDeviceCommService.Device.DebugEngine.GetAllNetworkConfigurations();

                            if (networkConfigurations.Count > 0)
                            {
                                deviceExplorer.DeviceNetworkConfiguration = networkConfigurations[0];
                            }
                            else
                            {
                                deviceExplorer.DeviceNetworkConfiguration = new DeviceConfiguration.NetworkConfigurationProperties();
                            }

                            // for now, just get the 1st Wi-Fi configuration, if exists
                            var wirellesConfigurations = NanoDeviceCommService.Device.DebugEngine.GetAllWireless80211Configurations();

                            if (wirellesConfigurations.Count > 0)
                            {
                                deviceExplorer.DeviceWireless80211Configuration = wirellesConfigurations[0];
                            }
                            else
                            {
                                deviceExplorer.DeviceWireless80211Configuration = new DeviceConfiguration.Wireless80211ConfigurationProperties();
                            }

                            // yield to give the UI thread a chance to respond to user input
                            await Task.Yield();

                            // need to switch to UI main thread
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                            if (networkConfigurations.Count == 0
                               && wirellesConfigurations.Count == 0)
                            {
                                // no network configuration available: device doesn't have network capabilities

                                _ = MessageBox.Show(
                                   "Connected nanoDevice doesn't have network capabilities. Can't open Network Configuration dialog.",
                                   ".NET nanoFramework Device Explorer",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);

                                // done here
                                return;
                            }

                            // show network configuration dialogue
                            var networkConfigDialog = new NetworkConfigurationDialog
                            {
                                HasMinimizeButton = false,
                                HasMaximizeButton = false
                            };
                            networkConfigDialog.ShowModal();
                        }
                        catch
                        {
                            // report issue to user
                            MessageCentre.OutputMessage($"Error reading {descriptionBackup} configurations.");

                            return;
                        }
                    }
                    else
                    {
                        MessageCentre.OutputMessage($"{descriptionBackup} is not responding, please reboot the device.");

                        return;
                    }
                }
                catch
                {
                    // OK to catch all
                }
                finally
                {
                    // disconnect device
                    NanoDeviceCommService.Device?.DebugEngine?.Stop(true);

                    exclusiveAccess?.Dispose();

                    // enable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(true);

                    // clear status bar
                    MessageCentre.StopProgressMessage();
                }
            });
        }

        /// <summary>
        /// Handler for RebootCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void RebootCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            await Task.Run(async delegate
            {
                GlobalExclusiveDeviceAccess exclusiveAccess = null;
                try
                {
                    // disable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(false);

                    var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

                    // make sure this device is showing as selected in Device Explorer tree view
                    deviceExplorer.ForceNanoDeviceSelection();

                    // Get exclusive access to the device, but don't wait forever
                    exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(deviceExplorer.SelectedDevice, ExclusiveAccessTimeout);
                    if (exclusiveAccess is null)
                    {
                        MessageCentre.OutputMessage($"Cannot access {deviceExplorer.SelectedDevice.Description}, another application is using the device.");
                        return;
                    }

                    // check if debugger engine exists
                    if (NanoDeviceCommService.Device.DebugEngine == null)
                    {
                        NanoDeviceCommService.Device.CreateDebugEngine();
                    }

                    var idCommand = (sender as MenuCommand).CommandID.ID;

                    // store the description
                    var previousSelectedDeviceDescription = NanoDeviceCommService.Device.Description;

                    // connect to the device
                    if (NanoDeviceCommService.Device.DebugEngine.Connect(
                        false,
                        true))
                    {
                        try
                        {
                            // reset the hash for the connected device so the deployment information can be refreshed, if and when requested
                            deviceExplorer.LastDeviceConnectedHash = 0;

                            // set reboot option according to the button that was clicked
                            RebootOptions rebootOption = RebootOptions.NormalReboot;

                            if (idCommand == GenerateCommandID(RebooMenuGroupID).ID)
                            {
                                // user clicked the "reboot group", let's choose the default (same as expected) reboot option
                                if (NanoDeviceCommService.Device.DebugEngine.IsConnectedTonanoCLR)
                                {
                                    // reboot CLR
                                    rebootOption = RebootOptions.ClrOnly;
                                }
                                else
                                {
                                    // reboot normally
                                    rebootOption = RebootOptions.NormalReboot;
                                }
                            }
                            else if (idCommand == GenerateCommandID(RebootClrID).ID)
                            {
                                rebootOption = RebootOptions.ClrOnly;
                            }
                            else if (idCommand == GenerateCommandID(RebootNanoBooterID).ID)
                            {
                                rebootOption = RebootOptions.EnterNanoBooter;
                            }
                            else if (idCommand == GenerateCommandID(RebootBootloaderID).ID)
                            {
                                rebootOption = RebootOptions.EnterProprietaryBooter;
                            }

                            MessageCentre.OutputMessage($"Sending reboot command to {previousSelectedDeviceDescription}.");

                            NanoDeviceCommService.Device.DebugEngine.RebootDevice(rebootOption);

                            // yield to give the UI thread a chance to respond to user input
                            await Task.Yield();

                            // reset property to force that device capabilities are retrieved on next connection
                            deviceExplorer.LastDeviceConnectedHash = 0;
                        }
                        catch
                        {
                            // report issue to user
                            MessageCentre.OutputMessage($"Error sending reboot command to {previousSelectedDeviceDescription}.");

                            return;
                        }
                    }
                    else
                    {
                        // reset property to force that device capabilities are retrieved on next connection
                        deviceExplorer.LastDeviceConnectedHash = 0;

                        MessageCentre.OutputMessage($"{previousSelectedDeviceDescription} is not responding, please reboot the device.");

                        return;
                    }
                }
                catch
                {
                    // OK to catch all
                }
                finally
                {
                    // disconnect device
                    NanoDeviceCommService.Device?.DebugEngine?.Stop(true);

                    exclusiveAccess?.Dispose();

                    // enable buttons
                    await UpdateDeviceDependentToolbarButtonsAsync(true);
                }
            });
        }


        /// <summary>
        /// Handler for RescanDevicesCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void RescanDevicesCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            await Task.Run(delegate
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                try
                {
                    NanoDeviceCommService.DebugClient.ReScanDevices();

                    // don't enable the button here to prevent compulsive developers to click it when the operation is still ongoing
                    // it will be enabled back at NanoDevicesCollectionChangedHandlerAsync
                }
                catch
                {
                    // empty on purpose, exceptions can happen during rescan

                    // OK to enable button here, in case an exception occurs
                    (sender as MenuCommand).Enabled = true;
                }
            });
        }

        private void ShowInternalErrorsCommandHandler(object sender, EventArgs e)
        {
            // save new status
            // the "Checked" property reflects the current state, the final value is the current one negated 
            // this is more a "changing" event rather then a "changed" one
            NanoFrameworkPackage.OptionShowInternalErrors = !(sender as MenuCommand).Checked;

            // toggle button checked state
            var currentCheckState = (sender as MenuCommand).Checked;
            (sender as MenuCommand).Checked = !currentCheckState;
        }

        private void DisableDeviceWatchersHandler(object sender, EventArgs e)
        {
            // save new status
            // the "Checked" property reflects the current state, the final value is the current one negated 
            // this is more a "changing" event rather then a "changed" one
            NanoFrameworkPackage.OptionDisableDeviceWatchers = !(sender as MenuCommand).Checked;

            var currentCheckState = (sender as MenuCommand).Checked;

            // call device port API 
            if (currentCheckState)
            {
                MessageCentre.InternalErrorWriteLine("Starting device watchers");

                NanoDeviceCommService.DebugClient.StartDeviceWatchers();

                // don't enable the rescan devices button as this will happen on device enumeration completed
            }
            else
            {
                MessageCentre.InternalErrorWriteLine("Stopping device watchers");

                NanoDeviceCommService.DebugClient.StopDeviceWatchers();

                var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

                // need to remove event handler
                NanoDeviceCommService.DebugClient.NanoFrameworkDevices.CollectionChanged -= deviceExplorer.NanoFrameworkDevices_CollectionChanged;

                MessageCentre.OutputMessage(Environment.NewLine);
                MessageCentre.OutputMessage("***********************************************************************************");
                MessageCentre.OutputMessage("** Device Watchers are DISABLED. You won't be able to connect to any nanoDevice. **");
                MessageCentre.OutputMessage("***********************************************************************************");
                MessageCentre.OutputMessage(Environment.NewLine);

                // set rescan devices button to disabled state
                MenuCommandService.FindCommand(GenerateCommandID(RescanDevicesCommandID)).Enabled = false;

                // alert user with message box to be perfectly clear on what has changed
                MessageBox.Show(
                    "Device Watchers are DISABLED. You won't be able to connect to any nanoDevice.",
                    ".NET nanoFramework Device Explorer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // toggle button checked state
            (sender as MenuCommand).Checked = !currentCheckState;
        }

        /// <summary>
        /// Handler for ShowSettingsCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void ShowSettingsCommandHandler(object sender, EventArgs arguments)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                // show settings dialogue
                var allSettingsDialog = new SettingsDialog
                {
                    HasMinimizeButton = false,
                    HasMaximizeButton = false
                };
                allSettingsDialog.ShowModal();
            }
            catch
            {
                // OK to catch all
            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // clear status bar
                MessageCentre.StopProgressMessage();
            }
        }


        #endregion

        public static void UpdateShowInternalErrorsButton(bool value)
        {
            var toolbarButtonCommandId = GenerateCommandID(ShowInternalErrorsCommandID);

            var menuItem = Instance.MenuCommandService.FindCommand(toolbarButtonCommandId);
            menuItem.Checked = value;
        }

        public static void UpdateDisableDeviceWatchersButton(bool value)
        {
            var toolbarButtonCommandId = GenerateCommandID(DisableDeviceWatchersCommandID);

            var menuItem = Instance.MenuCommandService.FindCommand(toolbarButtonCommandId);
            menuItem.Checked = value;

            // enable rescan devices button because rescan operation has completed
            Instance.MenuCommandService.FindCommand(GenerateCommandID(RescanDevicesCommandID)).Enabled = !value;
        }

        #region MVVM messaging handlers

        private async Task SelectedNanoDeviceHasChangedHandlerAsync()
        {
            await Task.Run(async delegate
            {
                var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

                if (deviceExplorer.SelectedDevice != null)
                {
                    NanoDeviceCommService.SelectDevice(deviceExplorer.SelectedDevice.Description);
                }
                else
                {
                    NanoDeviceCommService.SelectDevice(null);
                }

                // refresh toolbar buttons on availability change
                await RefreshToolbarButtonsAsync();
            });
        }

        private async Task NanoDevicesCollectionChangedHandlerAsync()
        {
            // refresh toolbar buttons on availability change
            await RefreshToolbarButtonsAsync();
        }

        private async Task NanoDevicesDeviceEnumerationCompletedHandlerAsync()
        {
            await Task.Run(async delegate
            {
                // if watchers are enabled, enable rescan devices button
                if (!NanoFrameworkPackage.OptionDisableDeviceWatchers)
                {
                    // switch to UI main thread
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // enable rescan devices button because rescan operation has completed
                    Instance.MenuCommandService.FindCommand(GenerateCommandID(RescanDevicesCommandID)).Enabled = true;
                }
            });
        }

        #endregion


        #region tool and status bar update and general managers

        private async Task RefreshToolbarButtonsAsync()
        {
            await Task.Run(async delegate
            {
                // switch to UI main thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // get the menu command service to reach the toolbar commands
                var menuCommandService = Instance.MenuCommandService;

                var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

                // are there any devices available
                if (deviceExplorer.AvailableDevices.Count > 0)
                {
                    // any device selected?
                    if (deviceExplorer.SelectedDevice != null)
                    {
                        // there is a device selected
                        // enable ping button
                        menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = true;
                        // enable capabilities button
                        menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = true;
                        // enable erase button
                        menuCommandService.FindCommand(GenerateCommandID(DeviceEraseID)).Enabled = true;
                        // enable network config button
                        menuCommandService.FindCommand(GenerateCommandID(NetworkConfigID)).Enabled = true;
                        // enable reboot menu group
                        menuCommandService.FindCommand(GenerateCommandID(RebooMenuGroupID)).Enabled = true;
                    }
                    else
                    {
                        // no device selected
                        // disable ping button
                        menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = false;
                        // disable capabilities button
                        menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = false;
                        // disable erase button
                        menuCommandService.FindCommand(GenerateCommandID(DeviceEraseID)).Enabled = false;
                        // disable network config button
                        menuCommandService.FindCommand(GenerateCommandID(NetworkConfigID)).Enabled = false;
                        // disable reboot menu group
                        menuCommandService.FindCommand(GenerateCommandID(RebooMenuGroupID)).Enabled = false;
                    }

                    // update reboot options
                    UpdateRebootMenuGroup(menuCommandService);
                }
                else
                {
                    // disable ping button
                    menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = false;
                    // disable capabilities button
                    menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = false;
                    // disable erase button
                    menuCommandService.FindCommand(GenerateCommandID(DeviceEraseID)).Enabled = false;
                    // disable network config button
                    menuCommandService.FindCommand(GenerateCommandID(NetworkConfigID)).Enabled = false;
                    // disable reboot menu group
                    menuCommandService.FindCommand(GenerateCommandID(RebooMenuGroupID)).Enabled = false;
                }
            });
        }

        private void UpdateRebootMenuGroup(OleMenuCommandService menuCommandService)
        {
            var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

            if (deviceExplorer.SelectedDevice != null)
            {
                // enable boot to nanoBooter, if available on target
                menuCommandService.FindCommand(GenerateCommandID(RebootNanoBooterID)).Enabled = deviceExplorer.SelectedDevice.HasNanoBooter;

                // enable boot to bootloader, if available on target
                menuCommandService.FindCommand(GenerateCommandID(RebootBootloaderID)).Enabled = deviceExplorer.SelectedDevice.HasProprietaryBooter;

                // enable boot CLR if we are on CLR
                menuCommandService.FindCommand(GenerateCommandID(RebootClrID)).Enabled = deviceExplorer.SelectedDevice.DebugEngine != null ? deviceExplorer.SelectedDevice.DebugEngine.IsConnectedTonanoCLR : false;
            }
            else
            {
                // enable everything, as default
                menuCommandService.FindCommand(GenerateCommandID(RebootNanoBooterID)).Enabled = true;
                menuCommandService.FindCommand(GenerateCommandID(RebootBootloaderID)).Enabled = true;
                menuCommandService.FindCommand(GenerateCommandID(RebootClrID)).Enabled = true;
            }
        }

        private async Task UpdateDeviceDependentToolbarButtonsAsync(bool isEnable)
        {
            await Task.Run(async delegate
            {
                // switch to UI main thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // get the menu command service to reach the toolbar commands
                var menuCommandService = Instance.MenuCommandService;

                // if we are to enable the buttons have to check 1st for device availability and selection
                // because these could have been changes when the command was executing
                // are there any devices available
                if (isEnable)
                {
                    var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

                    if (deviceExplorer.AvailableDevices.Count == 0)
                    {
                        // no device available!!
                        // done here
                        return;
                    }

                    // any device selected?
                    if (deviceExplorer.SelectedDevice == null)
                    {
                        // no device selected!!
                        // done here
                        return;
                    }
                }

                // now update button's enabled property as requested

                // ping button
                menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = isEnable;
                // capabilities button
                menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = isEnable;
                // erase button
                menuCommandService.FindCommand(GenerateCommandID(DeviceEraseID)).Enabled = isEnable;
                // network config button
                menuCommandService.FindCommand(GenerateCommandID(NetworkConfigID)).Enabled = isEnable;
                // reboot menu group
                menuCommandService.FindCommand(GenerateCommandID(RebooMenuGroupID)).Enabled = isEnable;

                // update reboot options
                UpdateRebootMenuGroup(menuCommandService);
            });
        }

        #endregion


        #region helper methods and utilities

        /// <summary>
        /// Generates a <see cref="CommandID"/> specific for the Device Explorer menugroup
        /// </summary>
        /// <param name="commandID">The ID for the command.</param>
        /// <returns></returns>
        private static CommandID GenerateCommandID(int commandID)
        {
            return new CommandID(new Guid(guidDeviceExplorerCmdSet), commandID);
        }

        #endregion
    }
}
