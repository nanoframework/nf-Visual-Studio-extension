//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Data context of the Device Explorer tool window.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="NanoFrameworkPackage.DeviceExplorerViewModel"/>, which owns the device state, and pushes the user's selection back to it.
    /// The commands run the <see cref="DeviceOperations"/>.
    /// </remarks>
    [DataContract]
    internal sealed class DeviceExplorerRemoteViewModel : NotifyPropertyChangedObject, IDisposable
    {
        private const string DeviceWatchersDisabledMessage = "Device watchers are disabled. Devices won't be found until they are enabled again.";

        private const string TroubleshootingUrl = "https://docs.nanoframework.net/content/getting-started-guides/trouble-shooting-guide.html";

        private readonly object _stateLock = new object();

        private DeviceExplorerViewModel _deviceExplorer;
        private bool _initialized;

        private DeviceItem _selectedDevice;
        private bool _hasDevices;
        private bool _showEmptyState;
        private bool _showProgress;
        private string _progressText;
        private string _infoBarText;
        private bool _showInfoBar;
        private bool _showInternalErrors;
        private bool _deviceWatchersDisabled;
        private bool _rebootOptionsAvailable;
        private bool _virtualDeviceEnabled;
        private bool _canToggleVirtualDevice;
        private string _virtualDeviceToggleText;
        private string _virtualDeviceToolTip;
        private bool _selectedDeviceIsVirtual;

        // the devices being read, or that couldn't be read, when they were selected
        private readonly HashSet<NanoDeviceBase> _devicesRead = new HashSet<NanoDeviceBase>();

        public DeviceExplorerRemoteViewModel()
        {
            PingCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.PingAsync());
            CapabilitiesCommand = new AsyncCommand(async (parameter, cancellationToken) =>
            {
                // read again from the device, the capabilities go to the Output window and the Properties window
                await DeviceOperations.QueryCapabilitiesAsync();

                await RefreshSelectedDeviceAsync(showPropertiesWindow: true);
            });
            EraseCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.EraseDeploymentAsync());
            NetworkConfigurationCommand = new AsyncCommand((parameter, cancellationToken) => EditNetworkConfigurationAsync());
            RebootCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.RebootAsync(null));
            RebootClrCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.RebootAsync(RebootOptions.ClrOnly));
            RebootNanoBooterCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.RebootAsync(RebootOptions.EnterNanoBooter));
            RebootBootloaderCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.RebootAsync(RebootOptions.EnterProprietaryBooter));
            RescanCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                DeviceOperations.RescanDevices();
                return Task.CompletedTask;
            });
            EnableDeviceWatchersCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                DeviceWatchersDisabled = false;
                return Task.CompletedTask;
            });
            RestartVirtualDeviceCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.RestartVirtualDeviceAsync());
            UpdateNanoClrCommand = new AsyncCommand((parameter, cancellationToken) => DeviceOperations.UpdateNanoClrAsync());
            SettingsCommand = new AsyncCommand((parameter, cancellationToken) => ShowSettingsAsync());
            CopyDetailsCommand = new AsyncCommand((parameter, cancellationToken) => CopyDetailsAsync());
            PropertiesCommand = new AsyncCommand(async (parameter, cancellationToken) =>
            {
                await DeviceOperations.ReadCapabilitiesIfNeededAsync();

                await RefreshSelectedDeviceAsync(showPropertiesWindow: true);
            });
            HelpCommand = new AsyncCommand((parameter, cancellationToken) => ShowHelpAsync());
            OpenLinkCommand = new AsyncCommand((parameter, cancellationToken) => OpenLinkAsync(parameter as string));

            UpdateCommandStates();
        }

        #region data members

        [DataMember]
        public ObservableList<DeviceItem> Devices { get; } = new ObservableList<DeviceItem>();

        /// <summary>
        /// The device selected in the list. Changes from the UI are pushed to <see cref="DeviceExplorerViewModel.SelectedDevice"/>.
        /// </summary>
        [DataMember]
        public DeviceItem SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    OnSelectedDeviceChangedByUser(value);
                }
            }
        }

        [DataMember]
        public bool HasDevices
        {
            get => _hasDevices;
            private set => SetProperty(ref _hasDevices, value);
        }

        [DataMember]
        public bool ShowEmptyState
        {
            get => _showEmptyState;
            private set => SetProperty(ref _showEmptyState, value);
        }

        [DataMember]
        public bool ShowProgress
        {
            get => _showProgress;
            private set => SetProperty(ref _showProgress, value);
        }

        [DataMember]
        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        [DataMember]
        public string InfoBarText
        {
            get => _infoBarText;
            private set => SetProperty(ref _infoBarText, value);
        }

        [DataMember]
        public bool ShowInfoBar
        {
            get => _showInfoBar;
            private set => SetProperty(ref _showInfoBar, value);
        }

        [DataMember]
        public bool ShowInternalErrors
        {
            get => _showInternalErrors;
            set
            {
                if (SetProperty(ref _showInternalErrors, value))
                {
                    DeviceOperations.SetShowInternalErrors(value);
                }
            }
        }

        [DataMember]
        public bool DeviceWatchersDisabled
        {
            get => _deviceWatchersDisabled;
            set
            {
                if (SetProperty(ref _deviceWatchersDisabled, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(DeviceWatchersEnabled));

                    UpdateInfoBar();
                    UpdateCommandStates();

                    // starting and stopping the watchers can take a while: the UI shows the new state right away
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            DeviceOperations.SetDeviceWatchersDisabled(value);
                        }
                        catch (Exception ex)
                        {
                            ExtensionTelemetry.TrackException(ex, "deviceexplorer.watchers");
                        }

                        UpdateCommandStates();
                    });
                }
            }
        }

        /// <summary>
        /// The opposite of <see cref="DeviceWatchersDisabled"/>, for the toggle button that is latched while the watchers are running.
        /// </summary>
        [DataMember]
        public bool DeviceWatchersEnabled
        {
            get => !_deviceWatchersDisabled;
            set => DeviceWatchersDisabled = !value;
        }

        /// <summary>
        /// The virtual device is enabled: it's started when it's turned on, and stopped when it's turned off.
        /// </summary>
        [DataMember]
        public bool VirtualDeviceEnabled
        {
            get => _virtualDeviceEnabled;
            set
            {
                if (SetProperty(ref _virtualDeviceEnabled, value))
                {
                    UpdateCommandStates();

                    DeviceOperations.SetVirtualDeviceEnabled(value);
                }
            }
        }

        /// <summary>
        /// The virtual device toggle is disabled while the virtual device is being set up, started or stopped.
        /// </summary>
        [DataMember]
        public bool CanToggleVirtualDevice
        {
            get => _canToggleVirtualDevice;
            private set => SetProperty(ref _canToggleVirtualDevice, value);
        }

        /// <summary>
        /// What the virtual device toggle does, for screen readers.
        /// </summary>
        [DataMember]
        public string VirtualDeviceToggleText
        {
            get => _virtualDeviceToggleText;
            private set => SetProperty(ref _virtualDeviceToggleText, value);
        }

        [DataMember]
        public string VirtualDeviceToolTip
        {
            get => _virtualDeviceToolTip;
            private set => SetProperty(ref _virtualDeviceToolTip, value);
        }

        /// <summary>
        /// The selected device is the virtual device, its context menu has the virtual device commands.
        /// </summary>
        [DataMember]
        public bool SelectedDeviceIsVirtual
        {
            get => _selectedDeviceIsVirtual;
            private set => SetProperty(ref _selectedDeviceIsVirtual, value);
        }

        /// <summary>
        /// At least one of the reboot options of the drop-down can be used.
        /// </summary>
        [DataMember]
        public bool RebootOptionsAvailable
        {
            get => _rebootOptionsAvailable;
            private set => SetProperty(ref _rebootOptionsAvailable, value);
        }

        [DataMember]
        public string TroubleshootingLink => TroubleshootingUrl;

        [DataMember]
        public AsyncCommand PingCommand { get; }

        [DataMember]
        public AsyncCommand CapabilitiesCommand { get; }

        [DataMember]
        public AsyncCommand EraseCommand { get; }

        [DataMember]
        public AsyncCommand NetworkConfigurationCommand { get; }

        [DataMember]
        public AsyncCommand RebootCommand { get; }

        [DataMember]
        public AsyncCommand RebootClrCommand { get; }

        [DataMember]
        public AsyncCommand RebootNanoBooterCommand { get; }

        [DataMember]
        public AsyncCommand RebootBootloaderCommand { get; }

        [DataMember]
        public AsyncCommand RescanCommand { get; }

        [DataMember]
        public AsyncCommand EnableDeviceWatchersCommand { get; }

        [DataMember]
        public AsyncCommand RestartVirtualDeviceCommand { get; }

        [DataMember]
        public AsyncCommand UpdateNanoClrCommand { get; }

        [DataMember]
        public AsyncCommand SettingsCommand { get; }

        [DataMember]
        public AsyncCommand CopyDetailsCommand { get; }

        [DataMember]
        public AsyncCommand PropertiesCommand { get; }

        [DataMember]
        public AsyncCommand HelpCommand { get; }

        [DataMember]
        public AsyncCommand OpenLinkCommand { get; }

        #endregion

        /// <summary>
        /// Loads the package, if needed, and starts mirroring the Device Explorer view model.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
            {
                return;
            }

            // the tool window can be restored before the package is loaded
            await EnsurePackageLoadedAsync(cancellationToken);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // checked again on the main thread, another call could have completed the initialization while this one was loading the package
            if (_initialized)
            {
                return;
            }

            var deviceExplorer = NanoFrameworkPackage.DeviceExplorerViewModel;

            if (deviceExplorer is null)
            {
                // not initialized, so the next time the control is loaded can try again
                MessageCentre.InternalErrorWriteLine("Device Explorer: the .NET nanoFramework package isn't initialized.");
                return;
            }

            _deviceExplorer = deviceExplorer;
            _initialized = true;

            _showInternalErrors = NanoSettings.ShowInternalErrors;
            _deviceWatchersDisabled = NanoSettings.DeviceWatchersDisabled;
            _virtualDeviceEnabled = NanoSettings.VirtualDeviceEnabled;
            RaiseNotifyPropertyChangedEvent(nameof(ShowInternalErrors));
            RaiseNotifyPropertyChangedEvent(nameof(DeviceWatchersDisabled));
            RaiseNotifyPropertyChangedEvent(nameof(DeviceWatchersEnabled));
            RaiseNotifyPropertyChangedEvent(nameof(VirtualDeviceEnabled));

            _deviceExplorer.PropertyChanged += DeviceExplorer_PropertyChanged;
            _deviceExplorer.AvailableDevices.CollectionChanged += AvailableDevices_CollectionChanged;

            DeviceOperations.StateChanged += DeviceOperations_StateChanged;
            NanoSettings.SettingChanged += NanoSettings_SettingChanged;

            SyncDevices();
            SyncSelection();
            UpdateInfoBar();
            UpdateCommandStates();
        }

        public void Dispose()
        {
            DeviceOperations.StateChanged -= DeviceOperations_StateChanged;
            NanoSettings.SettingChanged -= NanoSettings_SettingChanged;

            if (_deviceExplorer != null)
            {
                _deviceExplorer.PropertyChanged -= DeviceExplorer_PropertyChanged;
                _deviceExplorer.AvailableDevices.CollectionChanged -= AvailableDevices_CollectionChanged;
            }
        }

        #region mirroring of the Device Explorer view model

        // these handlers run on the main thread: that's where the Device Explorer view model is changed

        private void DeviceExplorer_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceExplorerViewModel.AvailableDevices))
            {
                // the collection was replaced
                if (sender is DeviceExplorerViewModel deviceExplorer)
                {
                    deviceExplorer.AvailableDevices.CollectionChanged -= AvailableDevices_CollectionChanged;
                    deviceExplorer.AvailableDevices.CollectionChanged += AvailableDevices_CollectionChanged;
                }

                SyncDevices();
                SyncSelection();
            }
            else if (e.PropertyName == nameof(DeviceExplorerViewModel.SelectedDevice))
            {
                SyncSelection();
            }
        }

        private void AvailableDevices_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncDevices();
            SyncSelection();
        }

        private void SyncDevices()
        {
            var devices = _deviceExplorer.AvailableDevices.ToList();

            // keep the items of the devices that are still there, so the selection in the UI isn't lost
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (!devices.Contains(Devices[i].Device))
                {
                    Devices.RemoveAt(i);
                }
            }

            foreach (var device in devices)
            {
                if (!Devices.Any(d => d.Device == device))
                {
                    Devices.Add(new DeviceItem(device));
                }
            }

            UpdateCommandStates();
        }

        private void SyncSelection()
        {
            var selected = _deviceExplorer.SelectedDevice;

            var item = selected is null ? null : Devices.FirstOrDefault(d => d.Device == selected);

            if (item != _selectedDevice)
            {
                // the backing field is set directly, this change doesn't come from the user
                _selectedDevice = item;
                RaiseNotifyPropertyChangedEvent(nameof(SelectedDevice));
            }

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await RefreshSelectedDeviceAsync(showPropertiesWindow: false);

                // the details of a device are read from it the first time it's selected, then they come from the cache
                if (selected != null
                    && DeviceOperations.GetCachedCapabilitiesData(selected) is null
                    && DeviceOperations.CanRunDeviceOperation
                    && _devicesRead.Add(selected))
                {
                    await DeviceOperations.ReadCapabilitiesIfNeededAsync();

                    // a device that couldn't be read isn't tried again when it's selected, one that was read is read again after it changes
                    if (DeviceOperations.GetCachedCapabilitiesData(selected) != null)
                    {
                        _devicesRead.Remove(selected);
                    }

                    await RefreshSelectedDeviceAsync(showPropertiesWindow: false);
                }
            });

            UpdateCommandStates();
        }

        private void OnSelectedDeviceChangedByUser(DeviceItem item)
        {
            // the list clears its selection when the selected device goes away, the Device Explorer view model handles that
            if (item is null)
            {
                return;
            }

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (_deviceExplorer != null
                    && _deviceExplorer.AvailableDevices.Contains(item.Device))
                {
                    _deviceExplorer.SelectedDevice = item.Device;
                }
            });
        }

        private void DeviceOperations_StateChanged(object sender, EventArgs e)
        {
            UpdateCommandStates();
        }

        // the settings can be changed from the options pages too
        private void NanoSettings_SettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NanoSettings.VirtualDeviceEnabled))
            {
                // the backing field is set directly, this change doesn't come from the toggle
                _virtualDeviceEnabled = NanoSettings.VirtualDeviceEnabled;
                RaiseNotifyPropertyChangedEvent(nameof(VirtualDeviceEnabled));

                UpdateCommandStates();
            }
        }

        #endregion

        #region state

        private void UpdateCommandStates()
        {
            // the state changes on any thread: one update at a time, so a stale one can't overwrite the latest
            lock (_stateLock)
            {
                UpdateCommandStatesCore();
            }
        }

        private void UpdateCommandStatesCore()
        {
            bool canRun = DeviceOperations.CanRunDeviceOperation;

            PingCommand.CanExecute = canRun;
            CapabilitiesCommand.CanExecute = canRun;
            EraseCommand.CanExecute = canRun;
            NetworkConfigurationCommand.CanExecute = canRun;
            RebootCommand.CanExecute = DeviceOperations.CanReboot(null);
            RebootClrCommand.CanExecute = DeviceOperations.CanReboot(RebootOptions.ClrOnly);
            RebootNanoBooterCommand.CanExecute = DeviceOperations.CanReboot(RebootOptions.EnterNanoBooter);
            RebootBootloaderCommand.CanExecute = DeviceOperations.CanReboot(RebootOptions.EnterProprietaryBooter);
            RebootOptionsAvailable = RebootClrCommand.CanExecute || RebootNanoBooterCommand.CanExecute || RebootBootloaderCommand.CanExecute;
            RescanCommand.CanExecute = _deviceExplorer != null && DeviceOperations.CanRescan;
            CopyDetailsCommand.CanExecute = _selectedDevice != null;
            PropertiesCommand.CanExecute = _selectedDevice != null;
            RestartVirtualDeviceCommand.CanExecute = DeviceOperations.CanRunVirtualDeviceOperation;
            UpdateNanoClrCommand.CanExecute = DeviceOperations.CanRunVirtualDeviceOperation;

            bool hasDevices = Devices.Count > 0;
            bool isEnumerating = DeviceOperations.IsEnumerating;
            bool virtualDeviceBusy = DeviceOperations.IsVirtualDeviceBusy;
            string operationText = DeviceOperations.OperationText;

            HasDevices = hasDevices;
            ShowEmptyState = !hasDevices && !isEnumerating;
            SelectedDeviceIsVirtual = _selectedDevice?.IsVirtual ?? false;

            UpdateVirtualDeviceToggle(virtualDeviceBusy);

            // the virtual device is also set up when Visual Studio starts, outside of the Device Explorer operations
            ProgressText = operationText
                ?? (virtualDeviceBusy ? "Setting up the virtual device..." : null)
                ?? (isEnumerating ? "Searching for devices..." : null);
            ShowProgress = ProgressText != null;
        }

        private void UpdateVirtualDeviceToggle(bool busy)
        {
            CanToggleVirtualDevice = DeviceOperations.CanToggleVirtualDevice;

            if (busy)
            {
                VirtualDeviceToggleText = _virtualDeviceEnabled ? "Stop virtual device" : "Start virtual device";
                VirtualDeviceToolTip = "Virtual device: busy, wait for the current operation to finish";
            }
            else if (_virtualDeviceEnabled)
            {
                VirtualDeviceToggleText = "Stop virtual device";
                VirtualDeviceToolTip = DeviceOperations.IsVirtualDeviceRunning
                    ? "Virtual device: running. Click to stop it"
                    : "Virtual device: enabled, not running. Click to disable it";
            }
            else
            {
                VirtualDeviceToggleText = "Start virtual device";
                VirtualDeviceToolTip = "Virtual device: stopped. Click to start a virtual device (nanoCLR) on this machine";
            }
        }

        private void UpdateInfoBar()
        {
            InfoBarText = _deviceWatchersDisabled ? DeviceWatchersDisabledMessage : null;
            ShowInfoBar = InfoBarText != null;
        }

        // the selected device is shown in the Properties window, and its tooltip has the same details
        private async Task RefreshSelectedDeviceAsync(bool showPropertiesWindow)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var item = _selectedDevice;

            // the details are read from the device objects, away from the main thread
            await TaskScheduler.Default;

            item?.Refresh();

            await DeviceProperties.PublishAsync(item?.Device, showPropertiesWindow);
        }

        #endregion

        #region commands

        private Task EditNetworkConfigurationAsync()
        {
            return DeviceOperations.EditNetworkConfigurationAsync(() =>
            {
                // show network configuration dialogue
                var networkConfigDialog = new NetworkConfigurationDialog(_deviceExplorer)
                {
                    HasMinimizeButton = false,
                    HasMaximizeButton = false
                };
                networkConfigDialog.ShowModal();
            });
        }

        private async Task ShowSettingsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "settings" } });

                // the changes are recorded by the options pages
                await NanoFrameworkPackage.ShowOptionPageAsync<NanoOptionsPageGeneral>();
            }
            catch (Exception ex)
            {
                // OK to catch all
                MessageCentre.InternalErrorWriteLine($"Error showing the options: {ex}");

                ExtensionTelemetry.TrackException(ex, "deviceexplorer.settings");
            }
        }

        private async Task ShowHelpAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "help" } });

                var helpDialog = new HelpDialog
                {
                    HasMinimizeButton = false,
                    HasMaximizeButton = false
                };
                helpDialog.ShowModal();
            }
            catch (Exception ex)
            {
                // OK to catch all
                MessageCentre.InternalErrorWriteLine($"Error showing the help dialog: {ex}");

                ExtensionTelemetry.TrackException(ex, "deviceexplorer.help");
            }
        }

        private async Task CopyDetailsAsync()
        {
            var device = _selectedDevice?.Device;

            if (device is null)
            {
                return;
            }

            var text = DeviceOperations.GetDeviceDetailsText(device);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                System.Windows.Clipboard.SetText(text);

                MessageCentre.OutputMessage($"Details of {device.Description} copied to the clipboard.");

                ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "copyDetails" } });
            }
            catch (Exception ex)
            {
                // the clipboard can be in use by another application
                MessageCentre.OutputMessage($"Couldn't copy the details of {device.Description} to the clipboard: {ex.Message}");
            }
        }

        private async Task OpenLinkAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            VsShellUtilities.OpenSystemBrowser(url);

            ExtensionTelemetry.TrackHyperlinkClicked(new Uri(url));
        }

        #endregion

        private static async Task EnsurePackageLoadedAsync(CancellationToken cancellationToken)
        {
            if (NanoFrameworkPackage.DeviceExplorerViewModel != null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsShell)) is Microsoft.VisualStudio.Shell.Interop.IVsShell7 shell)
            {
                var packageGuid = new Guid(NanoFrameworkPackage.PackageGuidString);

                await shell.LoadPackageAsync(ref packageGuid);
            }
        }
    }

    /// <summary>
    /// A device in the Device Explorer list.
    /// </summary>
    [DataContract]
    internal sealed class DeviceItem : NotifyPropertyChangedObject
    {
        private string _toolTip;

        public DeviceItem(NanoDeviceBase device)
        {
            Device = device;
            Description = device.Description;

            // virtual devices are on a serial port, but they are identified by their description
            IsVirtual = device.Description?.StartsWith("Virtual nanoDevice", StringComparison.Ordinal) ?? false;

            if (IsVirtual)
            {
                Icon = ImageMoniker.KnownValues.Processor;
                Kind = "Virtual device";
            }
            else if (device.Transport == TransportType.TcpIp)
            {
                Icon = ImageMoniker.KnownValues.Network;
                Kind = "Network device";
            }
            else
            {
                Icon = ImageMoniker.KnownValues.USB;
                Kind = "Serial device";
            }

            Refresh();
        }

        public NanoDeviceBase Device { get; }

        /// <summary>
        /// The device is the virtual device running on this machine.
        /// </summary>
        public bool IsVirtual { get; }

        [DataMember]
        public string Description { get; }

        [DataMember]
        public ImageMoniker Icon { get; }

        /// <summary>
        /// The kind of connection, for screen readers.
        /// </summary>
        [DataMember]
        public string Kind { get; }

        [DataMember]
        public string ToolTip
        {
            get => _toolTip;
            private set => SetProperty(ref _toolTip, value);
        }

        /// <summary>
        /// Reads the details again, they can change after the device is queried.
        /// </summary>
        public void Refresh()
        {
            var lines = DeviceOperations.GetDeviceDetails(Device)
                .Where(d => d.Key is "Target" or "Platform" or "Connection" or "CLR version" or "Firmware" or "Booter")
                .Select(d => $"{d.Key}: {d.Value}");

            ToolTip = string.Join(Environment.NewLine, new[] { Description }.Concat(lines));
        }

        public override string ToString() => Description;
    }
}
