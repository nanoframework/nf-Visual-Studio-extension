//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Messaging;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// <para>
    /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
    /// </para>
    /// <para>
    /// You can also use Blend to data bind with the tool's support.
    /// </para>
    /// <para>
    /// See http://www.galasoft.ch/mvvm
    /// </para>
    /// </summary>
    public class DeviceExplorerViewModel : ViewModelBase, INotifyPropertyChanging
    {
        public const int WRITE_TO_OUTPUT_TOKEN = 1;
        public const int SELECTED_NULL_TOKEN = 2;

        // for serial devices we wait 10 seconds for the device to be available again
        private const int SerialDeviceReconnectMaximumAttempts = 4 * 10;

        // keep this here otherwise Fody won't be able to properly implement INotifyPropertyChanging
#pragma warning disable 67
        public event PropertyChangingEventHandler PropertyChanging;
#pragma warning restore 67

        private bool _deviceEnumerationCompleted { get; set; }

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
        private IServiceProvider _serviceProvider
        {
            get
            {
                return Package;
            }
        }

        public INanoDeviceCommService NanoDeviceCommService { private get; set; }

        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public DeviceExplorerViewModel()
        {
            if (IsInDesignMode)
            {
                // Code runs in Blend --> create design time data.
                AvailableDevices = new ObservableCollection<NanoDeviceBase>();

                //AvailableDevices.Add(new NanoDevice<NanoSerialDevice>() { Description = "Awesome nanodevice1" });
                //AvailableDevices.Add(new NanoDevice<NanoSerialDevice>() { Description = "Awesome nanodevice2" });
            }
            else
            {
                // Code runs "for real"
                AvailableDevices = new ObservableCollection<NanoDeviceBase>();
            }

            SelectedDevice = null;
        }

        public ObservableCollection<NanoDeviceBase> AvailableDevices { set; get; }

        public NanoDeviceBase SelectedDevice { get; set; }

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

            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.NanoDevicesDeviceEnumerationCompleted);
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
                    MessengerInstance.Send(new NotificationMessage(d.ConnectionId.ToString()), MessagingTokens.LaunchFirmwareUpdateForNanoDevice);
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

            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.NanoDevicesCollectionHasChanged);

            // launch update for arriving devices, if any
            if (e.NewItems != null)
            {
                foreach (var d in e.NewItems)
                {
                    MessengerInstance.Send(new NotificationMessage((d as NanoDeviceBase).ConnectionId.ToString()), MessagingTokens.LaunchFirmwareUpdateForNanoDevice);
                }
            }

            // signal departure of devices removed, if any
            if (e.OldItems != null)
            {
                foreach (var d in e.OldItems)
                {
                    MessengerInstance.Send(new NotificationMessage((d as NanoDeviceBase).ConnectionId.ToString()), MessagingTokens.NanoDeviceHasDeparted);
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
            _ = Task.Run(() => { MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.ForceSelectionOfNanoDevice); });
        }

        public void ForceNanoDeviceSelection()
        {
            // request forced selection of device in UI
            _ = Task.Run(() => { MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.ForceSelectionOfNanoDevice); });
        }

        public void OnSelectedDeviceChanged()
        {
            // clear hash for connected device
            LastDeviceConnectedHash = 0;

            // signal event that the selected device has changed
            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.SelectedNanoDeviceHasChanged);
        }


        #region Transport

        public List<Debugger.WireProtocol.TransportType> AvailableTransportTypes { get; set; }

        public Debugger.WireProtocol.TransportType SelectedTransportType { get; set; }

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

        #endregion

        # region Network configuration dialog

        public DeviceConfiguration.NetworkConfigurationProperties DeviceNetworkConfiguration { get; set; }

        public DeviceConfiguration.Wireless80211ConfigurationProperties DeviceWireless80211Configuration { get; set; }

        public DeviceConfiguration.X509CaRootBundleProperties CaCertificateBundle { get; set; }
        public DeviceConfiguration.X509DeviceCertificatesProperties DeviceCertificate { get; internal set; }

        #endregion

        #region messaging tokens

        public static class MessagingTokens
        {
            public static readonly string SelectedNanoDeviceHasChanged = new Guid("{C3173983-A19A-49DD-A4BD-F25D360F7334}").ToString();
            public static readonly string NanoDevicesCollectionHasChanged = new Guid("{3E8906F9-F68A-45B7-A0CE-6D42BDB22455}").ToString();
            public static readonly string NanoDevicesDeviceEnumerationCompleted = new Guid("{347E2874-212C-4BC8-BB38-16E91FFCAB32}").ToString();
            public static readonly string ForceSelectionOfNanoDevice = new Guid("{8F012794-BC66-429D-9F9D-A9B0F546D6B5}").ToString();
            public static readonly string LaunchFirmwareUpdateForNanoDevice = new Guid("{93822E8C-4A94-4573-AC4F-DEB7FA703933}").ToString();
            public static readonly string NanoDeviceHasDeparted = new Guid("{38429FA1-3C16-44C2-937E-227C20AC0342}").ToString();
        }

        #endregion
    }
}
