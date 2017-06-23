//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight.Messaging;
using Microsoft.Practices.ServiceLocation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Windows.Forms;

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

        public const string guidDeviceExplorerCmdSet = "DF641D51-1E8C-48E4-B549-CC6BCA9BDE19";  // this GUID is comming from the .vsct file  

        public const int DeviceExplorerToolbarID = 0x1000;

        // toolbar commands
        public const int ConnectDeviceCommandID = 0x0200;
        public const int DisconnectDeviceCommandID = 0x0201;
        public const int PingDeviceCommandID = 0x0202;
        public const int DeviceCapabilitiesID = 0x0203;


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

            // launches the serial client and service
            var serialClient = CreateSerialDebugClient();
            ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().SerialDebugService = serialClient;
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
        private IServiceProvider ServiceProvider
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
        public static void Initialize(Package package, ViewModelLocator vmLocator)
        {
            Instance = new DeviceExplorerCommand(package);

            Instance.ViewModelLocator = vmLocator;
            //windowApp = ();

            Instance.CreateToolbarHandlers();

            // setup message listeners to be notifed of events occuring in the View Model
            Messenger.Default.Register<NotificationMessage>(Instance, DeviceExplorerViewModel.MessagingTokens.SelectedNanoDeviceHasChanged, (message) => Instance.SelectedNanoDeviceChangeHandler());
            Messenger.Default.Register<NotificationMessage>(Instance, DeviceExplorerViewModel.MessagingTokens.NanoDevicesCollectionHasChanged, (message) => Instance.NanoDevicesCollectionChangedHandler());
            Messenger.Default.Register<NotificationMessage>(Instance, DeviceExplorerViewModel.MessagingTokens.ConnectionStateResultHasChanged, (message) => Instance.ConnectionStateResultChangedHandler());
        }

        private void CreateToolbarHandlers()
        {
            // Create the handles for the toolbar commands
            var menuCommandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            // ConnectDeviceCommand
            var toolbarButtonCommandId = GenerateCommandID(ConnectDeviceCommandID);
            var menuItem = new MenuCommand(new EventHandler(
                ConnectDeviceCommandButtonHandler) , toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = true;
            menuCommandService.AddCommand(menuItem);

            // DisconnectDeviceCommand
            toolbarButtonCommandId = GenerateCommandID(DisconnectDeviceCommandID);
            menuItem = new MenuCommand(new EventHandler(
                DisconnectDeviceCommandHandler), toolbarButtonCommandId);
            menuItem.Enabled = false;
            menuItem.Visible = false;
            menuCommandService.AddCommand(menuItem);

            // PingCommand
            toolbarButtonCommandId = GenerateCommandID(PingDeviceCommandID);
            menuItem = new MenuCommand(new EventHandler(
                PingDeviceCommandHandlerAsync), toolbarButtonCommandId);
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
        /// Handler for ConnectDeviceCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private void ConnectDeviceCommandButtonHandler(object sender, EventArgs arguments)
        {
            var statusBar = this.ServiceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
            // this is long running operation so better show an animation to provide proper visual feedback to the developer
            // use the stock general animation icon
            object icon = (short)Constants.SBAI_General;

            UpdateStatusBar($"Connecting to {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}...");

            try
            {
                // start the animation
                statusBar?.Animation(1, ref icon);

                // disable the button
                (sender as MenuCommand).Enabled = false;

                // just in case the current device is connected
                //ViewModelLocator.DeviceExplorer.SelectedDevice?.DebugEngine.Disconnect();

                // now try to connect
                ViewModelLocator.DeviceExplorer.ConnectDisconnect();
            }
            catch(Exception ex)
            {
                
            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // stop the animation
                statusBar?.Animation(0, ref icon);

                ClearStatusBar();
            }
        }

        /// <summary>
        /// Handler for DisconnectDeviceCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private void DisconnectDeviceCommandHandler(object sender, EventArgs arguments)
        {
            UpdateStatusBar($"Disconnecting from {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}...");

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                ViewModelLocator.DeviceExplorer.ConnectDisconnect();
            }
            catch(Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                ClearStatusBar();
            }
        }

        /// <summary>
        /// Handler for PingDeviceCommand
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arguments"></param>
        /// <remarks>OK to use async void because this is a top-level event-handler 
        /// https://channel9.msdn.com/Series/Three-Essential-Tips-for-Async/Tip-1-Async-void-is-for-top-level-event-handlers-only
        /// </remarks>
        private async void PingDeviceCommandHandlerAsync(object sender, EventArgs arguments)
        {
            UpdateStatusBar($"Pinging {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}...");
            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                var pingResult = await ViewModelLocator.DeviceExplorer.SelectedDevice.PingAsync();

                IVsOutputWindowPane windowPane = (IVsOutputWindowPane)this.ServiceProvider.GetService(typeof(SVsGeneralOutputWindowPane));

                switch (pingResult)
                {
                    case PingConnectionType.NoConnection:
                        windowPane.OutputStringAsLine($"No reply from {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}");
                        break;

                    case PingConnectionType.NanoBooter:
                    case PingConnectionType.NanoCLR:
                        windowPane.OutputStringAsLine($"{ViewModelLocator.DeviceExplorer.SelectedDevice.Description} is active running {pingResult.ToString()}");
                        break;

                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                ClearStatusBar();
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
        private void DeviceCapabilitiesCommandHandler(object sender, EventArgs arguments)
        {
            UpdateStatusBar($"Querying {ViewModelLocator.DeviceExplorer.SelectedDevice.Description} capabilites...");

            var statusBar = this.ServiceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
            // this is long running operation so better show an animation to provide proper visual feedback to the developer
            // use the stock general animation icon
            object icon = (short)Constants.SBAI_General;

            try
            {
                // disable the button
                (sender as MenuCommand).Enabled = false;

                statusBar?.Animation(1, ref icon);

                ViewModelLocator.DeviceExplorer.LoadDeviceInfo();

                IVsOutputWindowPane windowPane = (IVsOutputWindowPane)this.ServiceProvider.GetService(typeof(SVsGeneralOutputWindowPane));

                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine("System Information");
                windowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceSystemInfo.ToString());
                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine(string.Empty);

                windowPane.OutputStringAsLine("--------------------------------");
                windowPane.OutputStringAsLine("::        Memory Map          ::");
                windowPane.OutputStringAsLine("--------------------------------");
                windowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceMemoryMap.ToString());
                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine("-----------------------------------------------------------");
                windowPane.OutputStringAsLine("::                   Flash Sector Map                    ::");
                windowPane.OutputStringAsLine("-----------------------------------------------------------");
                windowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceFlashSectorMap.ToString());
                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine(string.Empty);
                windowPane.OutputStringAsLine("Deployment Map");
                windowPane.OutputStringAsLine(ViewModelLocator.DeviceExplorer.DeviceDeploymentMap.ToString());
                windowPane.OutputStringAsLine(string.Empty);

            }
            catch (Exception ex)
            {

            }
            finally
            {
                // enable the button
                (sender as MenuCommand).Enabled = true;

                // stop the animation
                statusBar?.Animation(0, ref icon);

                ClearStatusBar();
            }
        }

        #endregion


        private INFSerialDebugClientService CreateSerialDebugClient()
        {
            // create serial instance WITHOUT app associated because we don't care of app life cycle in VS extension
            var serialDebugClient = PortBase.CreateInstanceForSerial("", null);

            return new NFSerialDebugClientService(serialDebugClient);
        }


        #region MVVM messaging handlers

        private void SelectedNanoDeviceChangeHandler()
        {
            // update toolbar 
            UpdateToolbarButtons();
        }

        private void NanoDevicesCollectionChangedHandler()
        {
            // update toolbar 
            UpdateToolbarButtons();
        }

        private void ConnectionStateResultChangedHandler()
        {
            // get the menu command service to reach the toolbar commands
            var menuCommandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            // get tge output window
            IVsOutputWindowPane windowPane = (IVsOutputWindowPane)this.ServiceProvider.GetService(typeof(SVsGeneralOutputWindowPane));

            // update toolbar according to current status
            if (ViewModelLocator.DeviceExplorer.ConnectionStateResult == ConnectionState.Connected)
            {
                // output message
                windowPane.OutputStringAsLine($"Connected to {ViewModelLocator.DeviceExplorer.SelectedDevice.Description}");

                // hide connect button
                menuCommandService.FindCommand(GenerateCommandID(ConnectDeviceCommandID)).Visible = false;
                // show disconnect button
                menuCommandService.FindCommand(GenerateCommandID(DisconnectDeviceCommandID)).Visible = true;

                // enable disconnect button
                menuCommandService.FindCommand(GenerateCommandID(DisconnectDeviceCommandID)).Enabled = true;
                // enable ping button
                menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = true;
                // enable capabilites button
                menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = true;
            }
            else if (ViewModelLocator.DeviceExplorer.ConnectionStateResult == ConnectionState.Disconnecting)
            {
                // disable ping button
                menuCommandService.FindCommand(GenerateCommandID(PingDeviceCommandID)).Enabled = false;
                // disable capabilites button
                menuCommandService.FindCommand(GenerateCommandID(DeviceCapabilitiesID)).Enabled = false;
            }
            else if (ViewModelLocator.DeviceExplorer.ConnectionStateResult == ConnectionState.Disconnected)
            {
                // output message, if there was a device selected
                if (ViewModelLocator.DeviceExplorer.PreviousSelectedDevice != null)
                {
                    windowPane.OutputStringAsLine($"Disconnected from {ViewModelLocator.DeviceExplorer.PreviousSelectedDevice.Description}");
                }

                // hide disconnect button
                menuCommandService.FindCommand(GenerateCommandID(DisconnectDeviceCommandID)).Visible = false;
                // show connect button
                menuCommandService.FindCommand(GenerateCommandID(ConnectDeviceCommandID)).Visible = true;
                // enable disconnect button
                menuCommandService.FindCommand(GenerateCommandID(DisconnectDeviceCommandID)).Enabled = true;
            }
            else
            {

            }
        }

        #endregion


        #region tool and status bar update and general managers

        private void UpdateToolbarButtons()
        {
            // get the menu command service to reach the toolbar commands
            var menuCommandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            // are there any devices available
            if (ViewModelLocator.DeviceExplorer.AvailableDevices.Count > 0)
            {
                // hide disconnect button
                menuCommandService.FindCommand(GenerateCommandID(DisconnectDeviceCommandID)).Visible = false;
                // show connect button
                menuCommandService.FindCommand(GenerateCommandID(ConnectDeviceCommandID)).Visible = true;

                // any device selected?
                if (ViewModelLocator.DeviceExplorer.SelectedDevice != null)
                {
                    // there is a device selected
                    // enable connect button
                    menuCommandService.FindCommand(GenerateCommandID(ConnectDeviceCommandID)).Enabled = true;
                }
                else
                {
                    // no device selected
                    // disable connect button
                    menuCommandService.FindCommand(GenerateCommandID(ConnectDeviceCommandID)).Enabled = false;
                }
            }
            else
            {
                // hide disconnect button
                menuCommandService.FindCommand(GenerateCommandID(DisconnectDeviceCommandID)).Visible = false;
                // disable connect button
                menuCommandService.FindCommand(GenerateCommandID(ConnectDeviceCommandID)).Enabled = false;
            }
        }

        private void UpdateStatusBar(string text)
        {
            var statusBar = this.ServiceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;

            // Make sure the status bar is not frozen  
            int frozen;
            statusBar.IsFrozen(out frozen);

            if (frozen != 0)
            {
                statusBar.FreezeOutput(0);
            }

            statusBar.SetText(text);
        }

        private void ClearStatusBar()
        {
            var statusBar = this.ServiceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;

            // Make sure the status bar is not frozen  
            int frozen;
            statusBar.IsFrozen(out frozen);

            if (frozen != 0)
            {
                statusBar.FreezeOutput(0);
            }

            //statusBar.Clear();
            statusBar.SetText(string.Empty);
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
