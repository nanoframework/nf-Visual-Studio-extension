//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// Bindable view of the network configuration properties edited in the network configuration dialog.
    /// </summary>
    /// <remarks>
    /// The debug library classes don't raise change notifications: the bound properties are kept here
    /// and copied to <see cref="Model"/> with <see cref="ApplyToModel"/> before saving.
    /// </remarks>
    public class NetworkConfigurationViewModel : ObservableObject
    {
        private byte[] _macAddress;
        private NetworkInterfaceType _interfaceType;

        public NetworkConfigurationViewModel(DeviceConfiguration.NetworkConfigurationProperties model)
        {
            Model = model;

            _macAddress = model.MacAddress;
            _interfaceType = model.InterfaceType;
        }

        /// <summary>
        /// The network configuration properties being edited.
        /// </summary>
        public DeviceConfiguration.NetworkConfigurationProperties Model { get; }

        public byte[] MacAddress
        {
            get => _macAddress;
            set => SetProperty(ref _macAddress, value);
        }

        public NetworkInterfaceType InterfaceType
        {
            get => _interfaceType;
            set => SetProperty(ref _interfaceType, value);
        }

        /// <summary>
        /// Copies the edited values to <see cref="Model"/>.
        /// </summary>
        public void ApplyToModel()
        {
            Model.MacAddress = MacAddress;
            Model.InterfaceType = InterfaceType;
        }
    }
}
