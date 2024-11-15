//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using static nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel.DeviceExplorerViewModel.Messages;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// </summary>
    public class DeviceExplorerViewModel : ObservableObject
    {
        public const int WRITE_TO_OUTPUT_TOKEN = 1;
        public const int SELECTED_NULL_TOKEN = 2;

        // for serial devices we wait 10 seconds for the device to be available again
        private const int SerialDeviceReconnectMaximumAttempts = 4 * 10;
        private ConnectionSource _lastDeviceConnectionSource;

        private bool _deviceEnumerationCompleted;

        /// <summary>
        /// Sets if Device Explorer should auto-select a device when there is only a single one in the available list.
        /// </summary>
        public bool AutoSelect { get; set; } = true;

        /// <summary>
        /// VS Package.
        /// </summary>
        public Package Package { get; set; }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider _serviceProvider => Package;

        private INanoDeviceCommService _nanoDeviceCommService;
        public INanoDeviceCommService NanoDeviceCommService
        {
            get => _nanoDeviceCommService;
            set
            {
                if (SetProperty(ref _nanoDeviceCommService, value))
                {
                    OnNanoDeviceCommServiceChanged();
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public DeviceExplorerViewModel()
        {
            AvailableDevices = new ObservableCollection<NanoDeviceBase>();
            SelectedDevice = null;
        }

        private ObservableCollection<NanoDeviceBase> _availableDevices;
        public ObservableCollection<NanoDeviceBase> AvailableDevices
        {
            get => _availableDevices;
            set => SetProperty(ref _availableDevices, value);
        }

        private NanoDeviceBase _selectedDevice;
        public NanoDeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        public string DeviceToReSelect { get; set; } = null;

        public void OnNanoDeviceCommServiceChanged()
        {
            if (NanoDeviceCommService != null)
            {
                NanoDeviceCommService.DebugClient.DeviceEnumerationCompleted += SerialDebugClient_DeviceEnumerationCompleted;
                NanoDeviceCommService.DebugClient.LogMessageAvailable += DebugClient_LogMessageAvailable;
            }
        }

        private void DebugClient_LogMessageAvailable(object sender, StringEventArgs e)
        {
            MessageCentre.InternalErrorWriteLine(e.EventText);
        }

        private void SerialDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            // save status
            _deviceEnumerationCompleted = true;

            SelectedTransportType = Debugger.WireProtocol.TransportType.Serial;

            UpdateAvailableDevices();

            WeakReferenceMessenger.Default.Send(new NanoDeviceEnumerationCompletedMessage());
        }

        private void UpdateAvailableDevices()
        {
            switch (SelectedTransportType)
            {
                case Debugger.WireProtocol.TransportType.Serial:
                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(NanoDeviceCommService.DebugClient.NanoFrameworkDevices);

                    // add handler, but make sure we aren't adding another one so remove it first
                    NanoDeviceCommService.DebugClient.NanoFrameworkDevices.CollectionChanged -= NanoFrameworkDevices_CollectionChanged;
                    NanoDeviceCommService.DebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                    break;

                case Debugger.WireProtocol.TransportType.Usb:
                    //AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.NanoFrameworkDevices);
                    //NanoDeviceCommService.NanoFrameworkDevicesCollectionChanged += NanoDeviceCommService_NanoFrameworkDevicesCollectionChanged;
                    break;

                case Debugger.WireProtocol.TransportType.TcpIp:
                    // TODO
                    //await Task.Delay(2500);
                    //    AvailableDevices = new ObservableCollection<NanoDeviceBase>();
                    //    SelectedDevice = null;
                    break;
            }

            // handle auto-connect option
            if (_deviceEnumerationCompleted || NanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete)
            {
                // this auto-connect can only run after the initial device enumeration is completed
                if (AutoSelect)
                {
                    // is there a single device
                    if (AvailableDevices.Count == 1)
                    {
                        ForceNanoDeviceSelection(AvailableDevices[0]);
                    }
                }

                // launch firmware update task
                foreach (var d in AvailableDevices)
                {
                    WeakReferenceMessenger.Default.Send(new LaunchFirmwareUpdateForNanoDeviceMessage(d.ConnectionId));
                }
            }
        }

        public void NanoFrameworkDevices_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // handle this according to the selected device type 
            switch (SelectedTransportType)
            {
                //case TransportType.Usb:
                //    AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.NanoFrameworkDevices);
                //    break;

                case Debugger.WireProtocol.TransportType.Serial:
                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(NanoDeviceCommService.DebugClient.NanoFrameworkDevices);
                    break;

                default:
                    // shouldn't get here...
                    break;
            }

            WeakReferenceMessenger.Default.Send(new NanoDevicesCollectionHasChangedMessage());

            // launch update for arriving devices, if any
            if (e.NewItems != null)
            {
                foreach (var d in e.NewItems)
                {
                    WeakReferenceMessenger.Default.Send(new LaunchFirmwareUpdateForNanoDeviceMessage((d as NanoDeviceBase).ConnectionId));
                }
            }

            // signal departure of devices removed, if any
            if (e.OldItems != null)
            {
                foreach (var d in e.OldItems)
                {
                    WeakReferenceMessenger.Default.Send(new NanoDeviceHasDepartedMessage((d as NanoDeviceBase).ConnectionId));
                }
            }

            // handle auto-select option
            if (_deviceEnumerationCompleted || NanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete)
            {
                // reselect a specific device has higher priority than auto-select
                if (DeviceToReSelect != null)
                {
                    var deviceToReSelect = AvailableDevices.FirstOrDefault(d => d.Description == DeviceToReSelect);
                    if (deviceToReSelect != null)
                    {
                        // device seems to be back online, select it
                        ForceNanoDeviceSelection(deviceToReSelect);

                        // clear device to reselect
                        DeviceToReSelect = null;
                    }
                }
                // this auto-select can only run after the initial device enumeration is completed
                else if (AutoSelect)
                {
                    // is there a single device
                    if (AvailableDevices.Count == 1)
                    {
                        ForceNanoDeviceSelection(AvailableDevices[0]);
                    }
                    else
                    {
                        // we have more than one now, was there any device already selected?
                        if (SelectedDevice != null)
                        {
                            // maintain selection
                            ForceNanoDeviceSelection(AvailableDevices.FirstOrDefault(d => d.Description == SelectedDevice.Description));
                        }
                    }
                }
            }
        }

        private void ForceNanoDeviceSelection(NanoDeviceBase nanoDevice)
        {
            // select the device
            SelectedDevice = nanoDevice;

            // request forced selection of device in UI
            _ = Task.Run(() => { WeakReferenceMessenger.Default.Send(new ForceSelectionOfNanoDeviceMessage()); });
        }

        public void ForceNanoDeviceSelection()
        {
            // request forced selection of device in UI
            _ = Task.Run(() => { WeakReferenceMessenger.Default.Send(new ForceSelectionOfNanoDeviceMessage()); });
        }

        public void OnSelectedDeviceChanged()
        {
            // clear hash for connected device
            LastDeviceConnectedHash = 0;

            // signal event that the selected device has changed
            WeakReferenceMessenger.Default.Send(new SelectedNanoDeviceHasChangedMessage());
        }

        #region Transport

        public List<Debugger.WireProtocol.TransportType> AvailableTransportTypes { get; set; }

        private Debugger.WireProtocol.TransportType _selectedTransportType;
        public Debugger.WireProtocol.TransportType SelectedTransportType
        {
            get => _selectedTransportType;
            set
            {
                if (SetProperty(ref _selectedTransportType, value))
                {
                    OnSelectedTransportTypeChanged();
                }
            }
        }

        public void OnSelectedTransportTypeChanged()
        {
            UpdateAvailableDevices();
        }

        #endregion

        #region Device Capabilities

        public StringBuilder DeviceDeploymentMap { get; set; }

        public StringBuilder DeviceFlashSectorMap { get; set; }

        public StringBuilder DeviceMemoryMap { get; set; }

        public StringBuilder DeviceSystemInfo { get; set; }

        public StringBuilder TargetInfo { get; internal set; }

        /// <summary>
        /// used to prevent repeated retrieval of device capabilities after connection
        /// </summary>
        public int LastDeviceConnectedHash { get; set; }

        /// <summary>
        /// used to store connection information about a previously connect device
        /// </summary>
        public ConnectionSource LastDeviceConnectionSource
        {
            get => LastDeviceConnectedHash != 0 ? _lastDeviceConnectionSource : ConnectionSource.Unknown;
            set => SetProperty(ref _lastDeviceConnectionSource, value);
        }

        #endregion

        #region Network configuration dialog

        public DeviceConfiguration.NetworkConfigurationProperties DeviceNetworkConfiguration { get; set; }

        public DeviceConfiguration.Wireless80211ConfigurationProperties DeviceWireless80211Configuration { get; set; }

        public DeviceConfiguration.X509CaRootBundleProperties CaCertificateBundle { get; set; }
        public DeviceConfiguration.X509DeviceCertificatesProperties DeviceCertificate { get; internal set; }

        public bool CanChangeMacAddress => SelectedDevice?.DebugEngine?.Capabilities?.CanChangeMacAddress ?? false;

        #endregion

        #region messaging tokens

        public sealed class Messages
        {
            public sealed class NanoDeviceEnumerationCompletedMessage
            {
            }

            public sealed class NanoDevicesCollectionHasChangedMessage
            {

            }

            public sealed class ForceSelectionOfNanoDeviceMessage
            {
            }

            public sealed class LaunchFirmwareUpdateForNanoDeviceMessage : ValueChangedMessage<string>
            {
                public LaunchFirmwareUpdateForNanoDeviceMessage(string value) : base(value)
                {
                }
            }

            public sealed class NanoDeviceHasDepartedMessage : ValueChangedMessage<string>
            {
                public NanoDeviceHasDepartedMessage(string value) : base(value)
                {
                }
            }

            public sealed class SelectedNanoDeviceHasChangedMessage
            {
            }

            public sealed class VirtualDeviceOperationExecutingMessage : ValueChangedMessage<bool>
            {
                public VirtualDeviceOperationExecutingMessage(bool value) : base(value)
                {
                }
            }

        }

        #endregion
    }
}
