//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Messaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;

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

        private BackgroundWorker _connectToNanoDevice;

        /// <summary>
        /// Sets if Device Explorer should auto-connect to a device when there is only a single one in the available list.
        /// </summary>
        public bool AutoConnect { get; set; } = true;

        /// <summary>
        /// VS Package.
        /// </summary>
        public Package Package;

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

            // initialize connect to background worker
            _connectToNanoDevice = new BackgroundWorker
            {
                WorkerReportsProgress = true,

                WorkerSupportsCancellation = true
            };
            _connectToNanoDevice.DoWork += ConnectToDeviceDoWork;
            _connectToNanoDevice.ProgressChanged += ConnectToDeviceProgressChanged;
            _connectToNanoDevice.RunWorkerCompleted += ConnectToNanoDeviceRunWorkerCompleted;
        }

        public INFSerialDebugClientService SerialDebugService { get; set; } = null;

        public ObservableCollection<NanoDeviceBase> AvailableDevices { set; get; }

        public NanoDeviceBase SelectedDevice { get; set; }

        public string PreviousSelectedDeviceDescription { get; private set; }

        public void OnSerialDebugServiceChanged()
        {
            if (SerialDebugService != null)
            {
                SerialDebugService.SerialDebugClient.DeviceEnumerationCompleted += SerialDebugClient_DeviceEnumerationCompleted;
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
                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(SerialDebugService.SerialDebugClient.NanoFrameworkDevices);
                    SerialDebugService.SerialDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                    //BusySrv.HideBusy();
                    break;

                case TransportType.Usb:
                    //BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                    //AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.UsbDebugClient.NanoFrameworkDevices);
                    //UsbDebugService.UsbDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
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
            if (_deviceEnumerationCompleted || SerialDebugService.SerialDebugClient.IsDevicesEnumerationComplete)
            {
                // this auto-connect can only run after the initial device enumeration is completed
                if (AutoConnect)
                {
                    // is there a single device
                    if (AvailableDevices.Count == 1)
                    {
                        // launch "connect to" worker
                        _connectToNanoDevice.RunWorkerAsync(AvailableDevices.First().Description);
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
                //    AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.UsbDebugClient.NanoFrameworkDevices);
                //    break;

                case TransportType.Serial:
                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(SerialDebugService.SerialDebugClient.NanoFrameworkDevices);
                    break;

                default:
                    // shouldn't get here...
                    break;
            }

            // signal event that the devices collection has changed
            this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(""), MessagingTokens.NanoDevicesCollectionHasChanged);

            // handle auto-connect option
            if (_deviceEnumerationCompleted || SerialDebugService.SerialDebugClient.IsDevicesEnumerationComplete)
            {
                // this auto-connect can only run after the initial device enumeration is completed
                if (AutoConnect)
                {
                    // is there a single device
                    if (AvailableDevices.Count == 1)
                    {
                        // check if a "connect to" worker is running
                        if (_connectToNanoDevice.IsBusy)
                        {
                            // request cancellation
                            _connectToNanoDevice.CancelAsync();
                        }

                        // launch "connect to" worker
                        _connectToNanoDevice.RunWorkerAsync(AvailableDevices.First().Description);
                    }
                }
            }
        }

        public void OnSelectedDeviceChanging()
        {
            // save previous device
            PreviousSelectedDeviceDescription = SelectedDevice?.Description;

            // disconnect device becoming unselected
            SelectedDeviceDisconnect();
        }

        public void OnSelectedDeviceChanged()
        {
            SelectedDeviceConnectionResult = PingConnectionResult.None;

            if (SelectedDevice != null)
            {
                SelectedDevice.DebugEngine.SpuriousCharactersReceived -= DebugEngine_SpuriousCharactersReceived;
                SelectedDevice.DebugEngine.SpuriousCharactersReceived += DebugEngine_SpuriousCharactersReceived;
            }

            // signal event that the selected device has changed
            this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(""), MessagingTokens.SelectedNanoDeviceHasChanged);
        }

        private void DebugEngine_SpuriousCharactersReceived(object sender, nanoFramework.Tools.Debugger.StringEventArgs e)
        {
            string textToSend = $"[{DateTime.Now.ToString()}] {e.EventText}";
            this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(textToSend), WRITE_TO_OUTPUT_TOKEN);
        }


        #region Transport

        public List<TransportType> AvailableTransportTypes { get; set; }

        public TransportType SelectedTransportType { get; set; }

        public void OnSelectedTransportTypeChanged()
        {
            UpdateAvailableDevices();
        }

        #endregion


        #region Ping

        public PingConnectionResult SelectedDeviceConnectionResult { get; set; }
        public bool ConnectionResultOk { get { return (SelectedDeviceConnectionResult == PingConnectionResult.Ok); } }
        public bool ConnectionResultError { get { return (SelectedDeviceConnectionResult == PingConnectionResult.Error); } }
        public bool Pinging { get { return (SelectedDeviceConnectionResult == PingConnectionResult.Busy); } }

        public void SelectedDevicePing()
        {
            SelectedDeviceConnectionResult = PingConnectionResult.Busy;

            ThreadHelper.JoinableTaskFactory.Run(async delegate {

                PingConnectionType connection = await SelectedDevice.PingAsync();

                SelectedDeviceConnectionResult = (connection != PingConnectionType.NoConnection) ? PingConnectionResult.Ok : PingConnectionResult.Error;

            });
        }

        #endregion


        #region Connect/Disconnect/Reconnect

        public ConnectionState ConnectionStateResult { get; set; } = ConnectionState.None;

        public bool Connected { get { return (ConnectionStateResult == ConnectionState.Connected); } }

        public bool Disconnected { get { return (ConnectionStateResult == ConnectionState.Disconnected); } }

        public bool Connecting { get { return (ConnectionStateResult == ConnectionState.Connecting); } }

        public bool Disconnecting { get { return (ConnectionStateResult == ConnectionState.Disconnecting); } }

        public void OnConnectionStateResultChanged()
        {
            // signal event that the connection state has changed
            this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(""), MessagingTokens.ConnectionStateResultHasChanged);
        }

        public void ConnectDisconnect()
        {
            if (ConnectionStateResult == ConnectionState.Connected)
            {
                SelectedDeviceDisconnect();
            }
            else
            {
                SelectedDeviceConnect();
            }
        }

        private void SelectedDeviceConnect()
        {
            if (SelectedDevice != null)
            {
                ConnectionStateResult = ConnectionState.Connecting;

                ThreadHelper.JoinableTaskFactory.Run(async delegate {

                    bool connectOk = await SelectedDevice.DebugEngine.ConnectAsync(1, 1000, true);

                    ConnectionStateResult = connectOk ? ConnectionState.Connected : ConnectionState.Disconnected;
                });
            }
        }

        private void SelectedDeviceDisconnect()
        {
            // only attempt to perform disconnect on a device if there is one 
            // AND if it's connection state is connected (no point on trying to disconnect something that is not connected, right?)
            if (SelectedDevice != null && ConnectionStateResult == ConnectionState.Connected)
            {
                // check if a "connect to" worker is running
                if (_connectToNanoDevice.IsBusy)
                {
                    // request cancellation
                    _connectToNanoDevice.CancelAsync();
                }

                ConnectionStateResult = ConnectionState.Disconnecting;

                // reset property to force that device capabilities are retrieved on next connection
                LastDeviceConnectedHash = 0;

                try
                {
                    SelectedDevice.DebugEngine.Disconnect();
                    ConnectionStateResult = ConnectionState.Disconnected;
                }
                catch(Exception ex)
                {
                    // TODO handle exception
                }
            }
        }

        public void RebootAndSetupReconnectToDevice()
        {
            // this is only possible to perform if there is a device connected 
            if (SelectedDevice != null && ConnectionStateResult == ConnectionState.Connected)
            {
                // store the device description
                var deviceToReconnect = SelectedDevice.Description;

                // save previous device
                PreviousSelectedDeviceDescription = deviceToReconnect;

                // check if a "connect to" worker is running
                if (_connectToNanoDevice.IsBusy)
                {
                    // request cancellation
                    _connectToNanoDevice.CancelAsync();
                }

                // reboot the device
                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    // remove device selection
                    // reset property to force that device capabilities are retrieved on next connection
                    LastDeviceConnectedHash = 0;
                    ConnectionStateResult = ConnectionState.Disconnecting;
                    ConnectionStateResult = ConnectionState.Disconnected;

                    await SelectedDevice.DebugEngine.RebootDeviceAsync(RebootOption.NormalReboot).ConfigureAwait(true);
                });

                // setup reconnection to device
                _connectToNanoDevice.RunWorkerAsync(deviceToReconnect);
            }
        }

        private void ConnectToNanoDeviceRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                IVsOutputWindowPane windowPane = (IVsOutputWindowPane)this._serviceProvider.GetService(typeof(SVsGeneralOutputWindowPane));
                windowPane.OutputStringAsLine(e.Error.Message);
            }
            else if (e.Result != null)
            {
                IVsOutputWindowPane windowPane = (IVsOutputWindowPane)this._serviceProvider.GetService(typeof(SVsGeneralOutputWindowPane));
                windowPane.OutputStringAsLine(e.Result.ToString());
            }
        }

        private void ConnectToDeviceProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            IVsOutputWindowPane windowPane = (IVsOutputWindowPane)this._serviceProvider.GetService(typeof(SVsGeneralOutputWindowPane));
            windowPane.OutputStringAsLine(e.UserState as string);
        }


        private void ConnectToDeviceDoWork(object sender, DoWorkEventArgs e)
        {
            // wait for 2 seconds
            Thread.Sleep(2000);

            // timeout counter
            int timeoutCounter = SerialDeviceReconnectMaximumAttempts;

            // attempts counter
            int attemptCounter = 0;

            int delayTime = 250;

            while (timeoutCounter > 0)
            {
                // check cancellation pending flag
                if ((sender as BackgroundWorker).CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                var deviceToConnect = AvailableDevices.FirstOrDefault(d => d.Description == e.Argument as string);

                if (deviceToConnect != null)
                {
                    // select device, if not already selected 
                    if (SelectedDevice == null || SelectedDevice.Description != deviceToConnect.Description)
                    {
                        SelectedDevice = deviceToConnect;
                    }

                    ThreadHelper.JoinableTaskFactory.Run(async delegate
                    {
                        // switch to UI main thread
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // signal event to force selection of device in Device Explorer
                        this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(""), MessagingTokens.ForceSelectionOfNanoDevice);
                    });

                    // try to connect to device
                    ConnectDisconnect();

                    // check connection
                    if (ConnectionStateResult == ConnectionState.Connected)
                    {
                        return;
                    }
                    else
                    {
                        // add attempt counter
                        attemptCounter++;

                        // increase delay
                        delayTime = (int)(1.25 * delayTime);
                    }
                }

                // check attempts counter
                if (attemptCounter > 3)
                {
                    // device is showing but can't connect after 10 attempts so it must gone crazy
                    e.Result = $"{e.Argument as string} seems unresponsive, try to reset the device...";
                    return;
                }

                // wait for 250ms
                Thread.Sleep(delayTime);

                timeoutCounter--;
            }

            e.Result = $"Couldn't connect to {e.Argument as string} before the set timeout...";
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

        public void LoadDeviceInfo(bool force = false)
        {
            // sanity check
            if (SelectedDevice == null)
            {
                return;
            }

            // if same device nothing to do here, exit
            if (SelectedDevice.Description.GetHashCode() == LastDeviceConnectedHash)
                return;

            // keep device description hash code to avoid get info twice
            LastDeviceConnectedHash = SelectedDevice.Description.GetHashCode();


            ThreadHelper.JoinableTaskFactory.Run(async delegate {

                try
                {
                    // get device info
                    var di = await SelectedDevice.GetDeviceInfoAsync(force);
                    var mm = await SelectedDevice.DebugEngine.GetMemoryMapAsync();
                    var fm = await SelectedDevice.DebugEngine.GetFlashSectorMapAsync();
                    var dm = await SelectedDevice.DebugEngine.GetDeploymentMapAsync();

                    // load properties for maps
                    DeviceMemoryMap = new StringBuilder(mm?.ToStringForOutput() ?? "Empty");
                    DeviceFlashSectorMap = new StringBuilder(fm?.ToStringForOutput() ?? "Empty");
                    DeviceDeploymentMap = new StringBuilder(dm?.ToStringForOutput() ?? "Empty");
                    // and system
                    DeviceSystemInfo = new StringBuilder(di?.ToString() ?? "Empty");
                }
                catch
                {
                    // reset property to force that device capabilities are retrieved on next connection
                    LastDeviceConnectedHash = 0;
                }

            });

        }

        #endregion


        #region messaging tokens

        public static class MessagingTokens
        {
            public static readonly Guid SelectedNanoDeviceHasChanged = new Guid("{C3173983-A19A-49DD-A4BD-F25D360F7334}");
            public static readonly Guid NanoDevicesCollectionHasChanged = new Guid("{3E8906F9-F68A-45B7-A0CE-6D42BDB22455}");
            public static readonly Guid ConnectionStateResultHasChanged = new Guid("{CBB58A61-51B0-4ABB-8484-5D44F84B6A3C}");
            public static readonly Guid ForceSelectionOfNanoDevice = new Guid("{8F012794-BC66-429D-9F9D-A9B0F546D6B5}");
        }

        #endregion

    }
}
