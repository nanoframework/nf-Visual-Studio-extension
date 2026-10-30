//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
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

        // devices already reported as connected in this session, see TrackDeviceConnected
        private static readonly HashSet<Guid> s_devicesSeen = new HashSet<Guid>();

        private ConnectionSource _lastDeviceConnectionSource;

        private bool _initialDeviceEnumerationCompleted;

        /// <summary>
        /// Sets if Device Explorer should auto-select a device when there is only a single one in the available list.
        /// </summary>
        public bool AutoSelect { get; set; } = true;

        /// <summary>
        /// VS Package.
        /// </summary>
        public Package Package { get; set; }

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

        // copy of AvailableDevices, replaced on every change, for the code running on other threads
        private NanoDeviceBase[] _availableDevicesSnapshot = Array.Empty<NanoDeviceBase>();

        public ObservableCollection<NanoDeviceBase> AvailableDevices
        {
            get => _availableDevices;
            set
            {
                if (SetProperty(ref _availableDevices, value))
                {
                    PublishAvailableDevicesSnapshot();
                }
            }
        }

        /// <summary>
        /// The devices in <see cref="AvailableDevices"/>, safe to read from any thread.
        /// </summary>
        /// <remarks>
        /// <see cref="AvailableDevices"/> is bound to the UI and changed on the UI thread only: code running on other threads has to use this copy.
        /// </remarks>
        public IReadOnlyList<NanoDeviceBase> AvailableDevicesSnapshot => Volatile.Read(ref _availableDevicesSnapshot);

        private void PublishAvailableDevicesSnapshot() =>
            Volatile.Write(ref _availableDevicesSnapshot, _availableDevices?.ToArray() ?? Array.Empty<NanoDeviceBase>());

        private NanoDeviceBase _selectedDevice;
        public NanoDeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    OnSelectedDeviceChanged();
                }
            }
        }

        public string DeviceToReSelect { get; set; } = null;

        public void OnNanoDeviceCommServiceChanged()
        {
            if (NanoDeviceCommService != null)
            {
                var devices = NanoDeviceCommService.DebugClient.NanoFrameworkDevices;

                NanoDeviceCommService.DebugClient.DeviceEnumerationCompleted += SerialDebugClient_DeviceEnumerationCompleted;
                NanoDeviceCommService.DebugClient.LogMessageAvailable += DebugClient_LogMessageAvailable;

                // subscribe before loading the current devices, so no change is missed
                devices.DeviceAdded += NanoFrameworkDevices_DeviceAdded;
                devices.DeviceRemoved += NanoFrameworkDevices_DeviceRemoved;

                AvailableDevices = new ObservableCollection<NanoDeviceBase>(devices);

                // the enumeration may have completed before the handlers above were attached
                if (NanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete)
                {
                    SerialDebugClient_DeviceEnumerationCompleted(this, EventArgs.Empty);
                }
            }
        }

        private void DebugClient_LogMessageAvailable(object sender, StringEventArgs e)
        {
            MessageCentre.InternalErrorWriteLine(e.EventText);
        }

        // the debug library raises these events on its own threads: AvailableDevices (bound to the UI) is only changed on the main thread

        private void SerialDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                OnDeviceEnumerationCompleted();
            });
        }

        private void NanoFrameworkDevices_DeviceAdded(object sender, NanoDeviceEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                OnDeviceAdded(e.Device);
            });
        }

        private void NanoFrameworkDevices_DeviceRemoved(object sender, NanoDeviceEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                OnDeviceRemoved(e.Device);
            });
        }

        private void OnDeviceEnumerationCompleted()
        {
            SelectedTransportType = TransportType.Serial;

            if (!_initialDeviceEnumerationCompleted)
            {
                HandlerDeviceSelection();

                // save status
                _initialDeviceEnumerationCompleted = true;
            }

            WeakReferenceMessenger.Default.Send(new NanoDeviceEnumerationCompletedMessage());

            MessageCentre.StopProgressMessage();
        }

        /// <summary>
        /// Records the first arrival of each device in this Visual Studio session.
        /// The device unique ID is only kept in memory to avoid duplicates, it's never sent.
        /// </summary>
        private static void TrackDeviceConnected(NanoDeviceBase device)
        {
            if (device is null)
            {
                return;
            }

            lock (s_devicesSeen)
            {
                if (!s_devicesSeen.Add(device.DeviceUniqueId))
                {
                    return;
                }
            }

            ExtensionTelemetry.TrackEvent("DeviceConnected", ExtensionTelemetry.DeviceProperties(device));
        }

        private void OnDeviceAdded(NanoDeviceBase device)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // the device may already be there, if it was added while loading the initial list
            if (AvailableDevices.Contains(device))
            {
                return;
            }

            AvailableDevices.Add(device);
            PublishAvailableDevicesSnapshot();

            WeakReferenceMessenger.Default.Send(new NanoDevicesCollectionHasChangedMessage());

            // launch update for the arriving device
            WeakReferenceMessenger.Default.Send(new LaunchFirmwareUpdateForNanoDeviceMessage(device.DeviceUniqueId.ToString()));

            TrackDeviceConnected(device);

            HandleAutoSelect();
        }

        private void OnDeviceRemoved(NanoDeviceBase device)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!AvailableDevices.Remove(device))
            {
                return;
            }

            PublishAvailableDevicesSnapshot();

            WeakReferenceMessenger.Default.Send(new NanoDevicesCollectionHasChangedMessage());

            // signal departure of the removed device
            WeakReferenceMessenger.Default.Send(new NanoDeviceHasDepartedMessage(device.ConnectionId));

            HandleAutoSelect();
        }

        private void HandleAutoSelect()
        {
            // handle auto-select option
            if (NanoDeviceCommService.DebugClient.IsDevicesEnumerationComplete)
            {
                HandlerDeviceSelection();
            }
        }

        private void HandlerDeviceSelection()
        {
            // reselect a specific device has higher priority than auto-select
            if (DeviceToReSelect != null)
            {
                NanoDeviceBase deviceToReSelect = AvailableDevices.FirstOrDefault(d => d.Description == DeviceToReSelect);

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

        // Device Explorer follows SelectedDevice, there's no need to force the selection in the UI
        private void ForceNanoDeviceSelection(NanoDeviceBase nanoDevice) => SelectedDevice = nanoDevice;

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

        }

        // this is required to properly handle device auto-selection when device watchers are re-enabled
        internal void ResetEnumerationFlag() => _initialDeviceEnumerationCompleted = false;

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

        #region messaging tokens

        public sealed class Messages
        {
            public sealed class NanoDeviceEnumerationCompletedMessage
            {
            }

            public sealed class NanoDevicesCollectionHasChangedMessage
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
        }

        #endregion
    }
}
