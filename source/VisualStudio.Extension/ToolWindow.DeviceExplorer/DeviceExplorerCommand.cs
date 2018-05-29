//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight.Messaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.ComponentModel.Design;
using System.Text;
using System.Threading;
using System.Windows.Forms;
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
        public static readonly Guid CommandSet = new Guid("c975c4ec-f229-45dd-b681-e42815641675");

        private ViewModelLocator ViewModelLocator;

        private static OleMenuCommandService _commandService;

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        private DeviceExplorer window;

        private Application windowApp;

        uint _statusBarCookie = 0;

        // command set Guids
        public const string guidDeviceExplorerCmdSet = "DF641D51-1E8C-48E4-B549-CC6BCA9BDE19";  // this GUID is coming from the .vsct file  

        public const int DeviceExplorerToolbarID = 0x1000;

        // toolbar commands
        public const int PingDeviceCommandID = 0x0210;
        public const int DeviceCapabilitiesID = 0x0220;
        public const int DeviceEraseID = 0x0230;
        public const int RebootID = 0x0240;
        public const int NetworkConfigID = 0x0250;

        // 2nd group
        public const int ShowInternalErrorsCommandID = 0x0300;


        INanoDeviceCommService NanoDeviceCommService;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceExplorerCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private DeviceExplorerCommand(Package package)
        {
            this.package = package ?? throw new ArgumentNullException("Package can't be null.");

            _commandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (_commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(ShowToolWindow, menuCommandID);
                _commandService.AddCommand(menuItem);
            }
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
        private System.IServiceProvider ServiceProvider
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
        public static void Initialize(Package package, ViewModelLocator vmLocator, INanoDeviceCommService nanoDeviceCommService)
        {
            Instance = new DeviceExplorerCommand(package);

            Instance.ViewModelLocator = vmLocator;
            Instance.NanoDeviceCommService = nanoDeviceCommService;

            //windowApp = ();

            Instance.CreateToolbarHandlers();

            // setup message listeners to be notified of events occurring in the View Model
            Messenger.Default.Register<NotificationMessage>(Instance, DeviceExplorerViewModel.MessagingTokens.SelectedNanoDeviceHasChanged, (message) => Instance.SelectedNanoDeviceHasChangedHandler());
            Messenger.Default.Register<NotificationMessage>(Instance, DeviceExplorerViewModel.MessagingTokens.NanoDevicesCollectionHasChanged, (message) => Instance.NanoDevicesCollectionChangedHandler());
        }

        private void CreateToolbarHandlers()
        {
            // Create the handles for the toolbar commands
            var menuCommandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

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
            menuItem = new MenuCommand( new EventHandler(
                DeviceCapabilitiesCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // DeviceErase
            toolbarButtonCommandId = GenerateCommandID(DeviceEraseID);
            menuItem = new MenuCommand( new EventHandler(
                DeviceEraseCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // Reboot
            toolbarButtonCommandId = GenerateCommandID(RebootID);
            menuItem = new MenuCommand( new EventHandler(
                RebootCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // NetworkConfig
            toolbarButtonCommandId = GenerateCommandID(NetworkConfigID);
            menuItem = new MenuCommand( new EventHandler(
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
            //menuItem.Checked = NanoFrameworkPackage.OptionShowInternalErrors;
            menuCommandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Shows the tool window when the menu item is clicked.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            // Get the instance number 0 of this tool window. This window is single instance so this instance
            // is actually the only one.
            // The last flag is set to true so that if the tool window does not exists it will be created.
            ToolWindowPane window = package.FindToolWindow(typeof(DeviceExplorer), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException("Cannot create nanoFramework Device Explorer tool window.");
            }

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
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

            MessageCentre.StartProgressMessage($"Pinging {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}...");
            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                // make sure this device is showing as selected in Device Explorer tree view
                ViewModelLocator.DeviceExplorer.ForceNanoDeviceSelection().FireAndForget();

                // connect to the device
                if (await NanoDeviceCommService.Device.DebugEngine.ConnectAsync(5000))
                {
                    // ping device
                    var connection = NanoDeviceCommService.Device.Ping();

                    switch (ViewModelLocator.DeviceExplorer.SelectedDevice.DebugEngine.ConnectionSource)
                    {
                        case Tools.Debugger.WireProtocol.ConnectionSource.Unknown:
                            MessageCentre.OutputMessage($"No reply from {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}");
                            break;

                        case Tools.Debugger.WireProtocol.ConnectionSource.nanoBooter:
                        case Tools.Debugger.WireProtocol.ConnectionSource.nanoCLR:
                            MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is active running {ViewModelLocator.DeviceExplorer.SelectedDevice.DebugEngine.ConnectionSource.ToString()}");
                            break;
                    }
                }
                else
                {
                    MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                MessageCentre.StopProgressMessage();
            }
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

            MessageCentre.StartProgressMessage($"Querying {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} capabilities...");

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                // make sure this device is showing as selected in Device Explorer tree view
                ViewModelLocator.DeviceExplorer.ForceNanoDeviceSelection().FireAndForget();

                // only query device if it's different 
                if (ViewModelLocator.DeviceExplorer.SelectedDevice.Description.GetHashCode() != ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash)
                {
                    // keep device description hash code to avoid get info twice
                    ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = ViewModelLocator.DeviceExplorer.SelectedDevice.Description.GetHashCode();


                    // connect to the device
                    if (await NanoDeviceCommService.Device.DebugEngine.ConnectAsync(5000))
                    {
                        try
                        {
                            // get device info
                            var deviceInfo = NanoDeviceCommService.Device.GetDeviceInfo(true);
                            var memoryMap = NanoDeviceCommService.Device.DebugEngine.GetMemoryMap();
                            var flashMap = NanoDeviceCommService.Device.DebugEngine.GetFlashSectorMap();
                            var deploymentMap = NanoDeviceCommService.Device.DebugEngine.GetDeploymentMap();

                            // we have to have a valid device info
                            if (deviceInfo.Valid)
                            {

                                // load view model properties for maps
                                ViewModelLocator.DeviceExplorer.DeviceMemoryMap = new StringBuilder(memoryMap?.ToStringForOutput() ?? "Empty");
                                ViewModelLocator.DeviceExplorer.DeviceFlashSectorMap = new StringBuilder(flashMap?.ToStringForOutput() ?? "Empty");
                                ViewModelLocator.DeviceExplorer.DeviceDeploymentMap = new StringBuilder(deploymentMap?.ToStringForOutput() ?? "Empty");

                                // load view model property for system
                                ViewModelLocator.DeviceExplorer.DeviceSystemInfo = new StringBuilder(deviceInfo?.ToString() ?? "Empty");
                            }
                            else
                            {
                                // reset property to force that device capabilities are retrieved on next connection
                                ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                                // report issue to user
                                MessageCentre.OutputMessage($"Error retrieving device information from { ViewModelLocator.DeviceExplorer.SelectedDevice.Description}. Please reconnect device.");

                                return;
                            }
                        }
                        catch
                        {
                            // reset property to force that device capabilities are retrieved on next connection
                            ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                            // report issue to user
                            MessageCentre.OutputMessage($"Error retrieving device information from { ViewModelLocator.DeviceExplorer.SelectedDevice.Description}. Please reconnect device.");

                            return;
                        }
                    }
                    else
                    {
                        // reset property to force that device capabilities are retrieved on next connection
                        ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                        MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");

                        return;
                    }
                }

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("System Information");
                MessageCentre.OutputMessage(ViewModelLocator.DeviceExplorer.DeviceSystemInfo.ToString());

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("--------------------------------");
                MessageCentre.OutputMessage("::        Memory Map          ::");
                MessageCentre.OutputMessage("--------------------------------");
                MessageCentre.OutputMessage(ViewModelLocator.DeviceExplorer.DeviceMemoryMap.ToString());

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("-----------------------------------------------------------");
                MessageCentre.OutputMessage("::                   Flash Sector Map                    ::");
                MessageCentre.OutputMessage("-----------------------------------------------------------");
                MessageCentre.OutputMessage(ViewModelLocator.DeviceExplorer.DeviceFlashSectorMap.ToString());

                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage(string.Empty);
                MessageCentre.OutputMessage("Deployment Map");
                MessageCentre.OutputMessage(ViewModelLocator.DeviceExplorer.DeviceDeploymentMap.ToString());
                MessageCentre.OutputMessage(string.Empty);

            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // clear status bar
                MessageCentre.StopProgressMessage();
            }
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

            MessageCentre.StartProgressMessage($"Erasing {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} deployment area...");

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                // make sure this device is showing as selected in Device Explorer tree view
                ViewModelLocator.DeviceExplorer.ForceNanoDeviceSelection().FireAndForget();

                // connect to the device
                if (await NanoDeviceCommService.Device.DebugEngine.ConnectAsync(5000))
                {
                    try
                    {
                        if (await NanoDeviceCommService.Device.EraseAsync(Debugger.EraseOptions.Deployment, CancellationToken.None))
                        {
                            MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} deployment area erased.");

                            // reset the hash for the connected device so the deployment information can be refreshed, if and when requested
                            ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                            // reboot device
                            NanoDeviceCommService.Device.DebugEngine.RebootDevice(Debugger.RebootOptions.ClrOnly | Debugger.RebootOptions.NoShutdown);

                            // yield to give the UI thread a chance to respond to user input
                            await Task.Yield();
                        }
                        else
                        {
                            // report issue to user
                            MessageCentre.OutputMessage($"Error erasing {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} deployment area.");
                        }
                    }
                    catch
                    {
                        // report issue to user
                        MessageCentre.OutputMessage($"Error erasing {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} deployment area.");

                        return;
                    }
                }
                else
                {
                    // reset property to force that device capabilities are retrieved on next connection
                    ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                    MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");

                    return;
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // clear status bar
                MessageCentre.StopProgressMessage();
            }
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

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                // make sure this device is showing as selected in Device Explorer tree view
                ViewModelLocator.DeviceExplorer.ForceNanoDeviceSelection().FireAndForget();

                // connect to the device
                if (await NanoDeviceCommService.Device.DebugEngine.ConnectAsync(5000))
                {
                    try
                    {
                        // for now, just get the 1st network configuration, if exists
                        var networkConfigurations = NanoDeviceCommService.Device.DebugEngine.GetAllNetworkConfigurations();

                        if (networkConfigurations.Count > 0)
                        {
                            ViewModelLocator.DeviceExplorer.DeviceNetworkConfiguration = networkConfigurations[0];
                        }
                        else
                        {
                            ViewModelLocator.DeviceExplorer.DeviceNetworkConfiguration = new Debugger.DeviceConfiguration.NetworkConfigurationProperties();
                        }
                        // yield to give the UI thread a chance to respond to user input
                        await Task.Yield();

                        // show network configuration dialogue
                        var networkConfigDialog = new NetworkConfigurationDialog();
                        networkConfigDialog.HasMinimizeButton = false;
                        networkConfigDialog.HasMaximizeButton = false;
                        networkConfigDialog.ShowModal();
                    }
                    catch
                    {
                        // report issue to user
                        MessageCentre.OutputMessage($"Error reading {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} configurations.");

                        return;
                    }
                }
                else
                {
                    MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");

                    return;
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // clear status bar
                MessageCentre.StopProgressMessage();
            }
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

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                // make sure this device is showing as selected in Device Explorer tree view
                ViewModelLocator.DeviceExplorer.ForceNanoDeviceSelection().FireAndForget();

                // connect to the device
                if (await NanoDeviceCommService.Device.DebugEngine.ConnectAsync(5000))
                {
                    try
                    {
                        NanoDeviceCommService.Device.DebugEngine.RebootDevice(Debugger.RebootOptions.NormalReboot);

                        MessageCentre.OutputMessage($"Sent reboot command to {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}.");

                        // reset the hash for the connected device so the deployment information can be refreshed, if and when requested
                        ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                        // yield to give the UI thread a chance to respond to user input
                        await Task.Yield();
                    }
                    catch
                    {
                        // report issue to user
                        MessageCentre.OutputMessage($"Error sending reboot command to {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}.");

                        return;
                    }
                }
                else
                {
                    // reset property to force that device capabilities are retrieved on next connection
                    ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                    MessageCentre.OutputMessage($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");

                    return;
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;
            }
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

        #endregion

        public static void UpdateShowInternalErrorsButton(bool value)
        {
            var toolbarButtonCommandId = GenerateCommandID(ShowInternalErrorsCommandID);
            var menuItem = _commandService.FindCommand(toolbarButtonCommandId);
            menuItem.Checked = value;
        }

        #region MVVM messaging handlers

        private void SelectedNanoDeviceHasChangedHandler()
        {
            if (ViewModelLocator.DeviceExplorer.SelectedDevice != null)
            {
                NanoDeviceCommService.SelectDevice(ViewModelLocator.DeviceExplorer.SelectedDevice.Description);
            }
            else
            {
                NanoDeviceCommService.SelectDevice(null);
            }

            // update toolbar 
            UpdateToolbarButtons();
        }

        private void NanoDevicesCollectionChangedHandler()
        {
            // update toolbar 
            UpdateToolbarButtons();
        }

        #endregion


        #region tool and status bar update and general managers

        private async void UpdateToolbarButtons()
        {
            // switch to UI main thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // get the menu command service to reach the toolbar commands
            var menuCommandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            // are there any devices available
            if (ViewModelLocator.DeviceExplorer.AvailableDevices.Count > 0)
            {
                // any device selected?
                if (ViewModelLocator.DeviceExplorer.SelectedDevice != null)
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
                    // enable reboot button
                    menuCommandService.FindCommand(GenerateCommandID(RebootID)).Enabled = true;
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
                    // disable reboot button
                    menuCommandService.FindCommand(GenerateCommandID(RebootID)).Enabled = false;
                }
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
                // disable reboot button
                menuCommandService.FindCommand(GenerateCommandID(RebootID)).Enabled = false;
            }
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
