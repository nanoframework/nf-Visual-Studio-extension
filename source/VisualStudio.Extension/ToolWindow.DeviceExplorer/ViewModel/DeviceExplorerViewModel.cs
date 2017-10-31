//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Messaging;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
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
    public class DeviceExplorerViewModel : ViewModelBase, INotifyPropertyChanging, INotifyPropertyChanged
    {
        public const int WRITE_TO_OUTPUT_TOKEN = 1;
        public const int SELECTED_NULL_TOKEN = 2;

        // for serial devices we wait 10 seconds for the device to be available again
        private const int SerialDeviceReconnectMaximumAttempts = 4 * 10;

        // keep this here otherwise Fody won't be able to properly implement INotifyPropertyChanging
#pragma warning disable 67
        public event PropertyChangingEventHandler PropertyChanging;
#pragma warning restore 67

        private bool _deviceEnumerationCompleted { get;  set; }

        CancellationTokenSource _cancelConnectToDevice = new CancellationTokenSource();

        private ConfiguredTaskAwaitable _connectToDeviceTask = new ConfiguredTaskAwaitable();

        /// <summary>
        /// Sets if Device Explorer should auto-connect to a device when there is only a single one in the available list.
        /// </summary>
        public bool AutoConnect { get; set; } = true;

        /// <summary>
        /// VS Package.
        /// </summary>
        public Package Package { get;  set; }

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

                AvailableDevices.Add(new NanoDevice<NanoSerialDevice>() { Description = "Awesome nanodevice1" });
                AvailableDevices.Add(new NanoDevice<NanoSerialDevice>() { Description = "Awesome nanodevice2" });
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

        public string DeviceToReconnect { get; set; } = null;

        public string PreviousSelectedDeviceDescription { get; internal set; }

        public void OnNanoDeviceCommServiceChanged()
        {
            if (NanoDeviceCommService != null)
            {
                NanoDeviceCommService.DebugClient.DeviceEnumerationCompleted += SerialDebugClient_DeviceEnumerationCompleted;
            }
        }

        private void SerialDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            // save status
            _deviceEnumerationCompleted = true;

            SelectedTransportType = TransportType.Serial;

            UpdateAvailableDevices();
        }

        private void UpdateAvailableDevices()
        {
            switch (SelectedTransportType)
            {
                case TransportType.Serial:
                    //BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(NanoDeviceCommService.DebugClient.NanoFrameworkDevices);
                    NanoDeviceCommService.DebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                    //NanoDeviceCommService.SerialDebugClient.NanoFrameworkDevicesCollectionChanged += NanoDeviceCommService_NanoFrameworkDevicesCollectionChanged;
                    //BusySrv.HideBusy();
                    break;

                case TransportType.Usb:
                    //BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                    //AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.NanoFrameworkDevices);
                    //NanoDeviceCommService.NanoFrameworkDevicesCollectionChanged += NanoDeviceCommService_NanoFrameworkDevicesCollectionChanged;
                    // if there's just one, select it
                    //SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;
                    //BusySrv.HideBusy();
                    break;

                case TransportType.TcpIp:
                    // TODO
                    //BusySrv.ShowBusy("Not implemented yet! Why not give it a try??");
                    //await Task.Delay(2500);
                    //    AvailableDevices = new ObservableCollection<NanoDeviceBase>();
                    //    SelectedDevice = null;
                    //BusySrv.HideBusy();
                    break;
            }

            // handle auto-connect option
            if (_deviceEnumerationCompleted || NanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete)
            {
                // this auto-connect can only run after the initial device enumeration is completed
                if (AutoConnect)
                {
                    // is there a single device
                    if (AvailableDevices.Count == 1)
                    {
                        ForceConnectionToNanoDevice(AvailableDevices[0]).ConfigureAwait(false);
                    }
                }
            }
        }

        private void NanoFrameworkDevices_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
             // handle this according to the selected device type 
            switch (SelectedTransportType)
            {
                //case TransportType.Usb:
                //    AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.NanoFrameworkDevices);
                //    break;

                case TransportType.Serial:
                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(NanoDeviceCommService.DebugClient.NanoFrameworkDevices);
                    break;

                default:
                    // shouldn't get here...
                    break;
            }

            // verify if the connected device was removed from the collection
            if (AvailableDevices.FirstOrDefault(d => d.Description == PreviousSelectedDeviceDescription) == null)
            {
                // update property because the device is not connected anymore
                SelectedDeviceConnectionState = ConnectionState.Disconnected;
            }

            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.NanoDevicesCollectionHasChanged);

            // handle auto-connect option
            if (_deviceEnumerationCompleted || NanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete)
            {
                // reconnect to a specific device has higher priority than auto-connect
                if(DeviceToReconnect != null)
                {
                    var deviceToReconnect = AvailableDevices.FirstOrDefault(d => d.Description == DeviceToReconnect);
                    if (deviceToReconnect != null)
                    {
                        // device seems to be back online, connect to it
                        ForceConnectionToNanoDevice(deviceToReconnect).ConfigureAwait(false);

                        // clear device to reconnect
                        DeviceToReconnect = null;
                    }
                }
                // this auto-connect can only run after the initial device enumeration is completed
                else if (AutoConnect)
                {
                    // is there a single device
                    if (AvailableDevices.Count == 1)
                    {
                        ForceConnectionToNanoDevice(AvailableDevices[0]).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task ForceConnectionToNanoDevice(NanoDeviceBase nanoDevice)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // select the device
            SelectedDevice = nanoDevice;

            // launch connect task from Device Explorer command
            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.ConnectToSelectedNanoDevice);

            // request forced selection of device in UI
            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.ForceSelectionOfNanoDevice);
        }

        public void OnSelectedDeviceChanging()
        {
            // save previous device
            PreviousSelectedDeviceDescription = SelectedDevice?.Description;
        }

        public void OnSelectedDeviceChanged()
        {
            SelectedDeviceConnectionState = ConnectionState.Disconnected;

            if (SelectedDevice != null)
            {
                SelectedDevice.DebugEngine.SpuriousCharactersReceived -= DebugEngine_SpuriousCharactersReceived;
                SelectedDevice.DebugEngine.SpuriousCharactersReceived += DebugEngine_SpuriousCharactersReceived;
            }

            // signal event that the selected device has changed
            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.SelectedNanoDeviceHasChanged);
        }

        private void DebugEngine_SpuriousCharactersReceived(object sender, nanoFramework.Tools.Debugger.StringEventArgs e)
        {
            string textToSend = $"[{DateTime.Now.ToString()}] {e.EventText}";
            MessengerInstance.Send(new NotificationMessage(textToSend), WRITE_TO_OUTPUT_TOKEN);
        }


        #region Transport

        public List<TransportType> AvailableTransportTypes { get; set; }

        public TransportType SelectedTransportType { get; set; }

        public void OnSelectedTransportTypeChanged()
        {
            UpdateAvailableDevices();
        }

        #endregion


        #region Connect/Disconnect/Reconnect

        public ConnectionState SelectedDeviceConnectionState { get; set; } = ConnectionState.None;

        public bool Connected { get { return (SelectedDeviceConnectionState == ConnectionState.Connected); } }

        public bool Disconnected { get { return (SelectedDeviceConnectionState == ConnectionState.Disconnected); } }

        public bool Connecting { get { return (SelectedDeviceConnectionState == ConnectionState.Connecting); } }

        public void OnSelectedDeviceConnectionStateChanged()
        {
            // signal event that the connection state has changed
            MessengerInstance.Send(new NotificationMessage(""), MessagingTokens.SelectedDeviceConnectionStateHasChanged);
        }
       
        public void RebootSelectedDevice()
        {
            // this is only possible to perform if there is a device connected 
            if (SelectedDevice != null && SelectedDeviceConnectionState == ConnectionState.Connected)
            {
                // save previous device
                PreviousSelectedDeviceDescription = DeviceToReconnect;

                // reboot the device
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    // remove device selection
                    // reset property to force that device capabilities are retrieved on next connection
                    LastDeviceConnectedHash = 0;
                    SelectedDeviceConnectionState = ConnectionState.Disconnected;

                    await SelectedDevice.DebugEngine.RebootDeviceAsync(RebootOption.NormalReboot).ConfigureAwait(true);
                });
            }
        }

        #endregion


        #region Device Capabilities

        public StringBuilder DeviceDeploymentMap { get; set; }

        public StringBuilder DeviceFlashSectorMap { get; set; }

        public StringBuilder DeviceMemoryMap { get; set; }

        public StringBuilder DeviceSystemInfo { get; set; }

        /// <summary>
        /// used to prevent repeated retrieval of device capabilities after connection
        /// </summary>
        public int LastDeviceConnectedHash { get; set; }

        #endregion


        #region messaging tokens

        public static class MessagingTokens
        {
            public static readonly string SelectedNanoDeviceHasChanged = new Guid("{C3173983-A19A-49DD-A4BD-F25D360F7334}").ToString();
            public static readonly string NanoDevicesCollectionHasChanged = new Guid("{3E8906F9-F68A-45B7-A0CE-6D42BDB22455}").ToString();
            public static readonly string SelectedDeviceConnectionStateHasChanged = new Guid("{CBB58A61-51B0-4ABB-8484-5D44F84B6A3C}").ToString();
            public static readonly string ForceSelectionOfNanoDevice = new Guid("{8F012794-BC66-429D-9F9D-A9B0F546D6B5}").ToString();
            public static readonly string ConnectToSelectedNanoDevice = new Guid("{63A8228F-99A4-44D1-B660-559A0D1E1965}").ToString();
        }

        #endregion
    }
}
