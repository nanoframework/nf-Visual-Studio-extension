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
        public const int PingDeviceCommandID = 0x0202;
        public const int DeviceCapabilitiesID = 0x0203;


        INanoDeviceCommService NanoDeviceCommService;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceExplorerCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private DeviceExplorerCommand(Package package)
        {
            this.package = package ?? throw new ArgumentNullException("Package can't be null.");

            OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(this.ShowToolWindow, menuCommandID);
                commandService.AddCommand(menuItem);
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
                return this.package;
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
            var menuCommandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

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
            ToolWindowPane window = this.package.FindToolWindow(typeof(DeviceExplorer), 0, true);
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

            NanoFrameworkPackage.StatusBar.Update($"Pinging {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}...");
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
                    var connection = await NanoDeviceCommService.Device.PingAsync().ConfigureAwait(true);

                    switch (ViewModelLocator.DeviceExplorer.SelectedDevice.DebugEngine.ConnectionSource)
                    {
                        case Debugger.WireProtocol.ConnectionSource.Unknown:
                            NanoFrameworkPackage.WindowPane.OutputStringAsLine($"No reply from {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}");
                            break;

                        case Debugger.WireProtocol.ConnectionSource.nanoBooter:
                        case Debugger.WireProtocol.ConnectionSource.nanoCLR:
                            NanoFrameworkPackage.WindowPane.OutputStringAsLine($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is active running {ViewModelLocator.DeviceExplorer.SelectedDevice.DebugEngine.ConnectionSource.ToString()}");
                            break;
                    }

                    // disconnect from the device
                    NanoDeviceCommService.Device.DebugEngine.Disconnect();
                }
                else
                {
                    NanoFrameworkPackage.WindowPane.OutputStringAsLine($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                IVsStatusbarExtensions.Clear(NanoFrameworkPackage.StatusBar);
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

            // this is long running operation so better show an animation to provide proper visual feedback to the developer
            NanoFrameworkPackage.StatusBar.Update($"Querying {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} capabilities...", true);

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
                            var deviceInfo = await NanoDeviceCommService.Device.GetDeviceInfoAsync(true);
                            var memoryMap = await NanoDeviceCommService.Device.DebugEngine.GetMemoryMapAsync();
                            var flashMap = await NanoDeviceCommService.Device.DebugEngine.GetFlashSectorMapAsync();
                            var deploymentMap = await NanoDeviceCommService.Device.DebugEngine.GetDeploymentMapAsync();

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
                                NanoFrameworkPackage.WindowPane.OutputStringAsLine($"Error retrieving device information from { ViewModelLocator.DeviceExplorer.SelectedDevice.Description}. Please reconnect device.");

                                return;
                            }
                        }
                        catch
                        {
                            // reset property to force that device capabilities are retrieved on next connection
                            ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                            // report issue to user
                            NanoFrameworkPackage.WindowPane.OutputStringAsLine($"Error retrieving device information from { ViewModelLocator.DeviceExplorer.SelectedDevice.Description}. Please reconnect device.");

                            return;
                        }
                        finally
                        {
                            // disconnect from the device
                            NanoDeviceCommService.Device.DebugEngine.Disconnect();
                        }
                    }
                    else
                    {
                        // reset property to force that device capabilities are retrieved on next connection
                        ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                        NanoFrameworkPackage.WindowPane.OutputStringAsLine($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding, please reboot the device.");

                        return;
                    }
                }

                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("System Information");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceSystemInfo.ToString());

                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("--------------------------------");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("::        Memory Map          ::");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("--------------------------------");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceMemoryMap.ToString());

                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("-----------------------------------------------------------");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("::                   Flash Sector Map                    ::");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("-----------------------------------------------------------");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceFlashSectorMap.ToString());

                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);
                NanoFrameworkPackage.WindowPane.OutputStringAsLine("Deployment Map");
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceDeploymentMap.ToString());
                NanoFrameworkPackage.WindowPane.OutputStringAsLine(string.Empty);

            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // clear status bar
                IVsStatusbarExtensions.Clear(NanoFrameworkPackage.StatusBar);
            }
        }

        #endregion


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
            var menuCommandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

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
                }
                else
                {
                    // no device selected
                    // disable ping button
                    menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = false;
                    // disable capabilities button
                    menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = false;
                }
            }
            else
            {
                // disable ping button
                menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = false;
                // disable capabilities button
                menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = false;
            }
        }

        #endregion


        #region helper methods and utilities

        /// <summary>
        /// Generates a <see cref="CommandID"/> specific for the Device Explorer menugroup
        /// </summary>
        /// <param name="commandID">The ID for the command.</param>
        /// <returns></returns>
        private CommandID GenerateCommandID(int commandID)
        {
            return new CommandID(new Guid(guidDeviceExplorerCmdSet), commandID);
        }

        #endregion
    }
}
