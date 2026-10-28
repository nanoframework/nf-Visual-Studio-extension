//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// Bindable view of the Wi-Fi profile properties edited in the network configuration dialog.
    /// </summary>
    /// <remarks>
    /// The debug library classes don't raise change notifications: the bound properties are kept here
    /// and copied to <see cref="Model"/> with <see cref="ApplyToModel"/> before saving.
    /// The password isn't bound (PasswordBox): it's set on <see cref="Model"/> directly.
    /// </remarks>
    public class Wireless80211ConfigurationViewModel : ObservableObject
    {
        private string _ssid;
        private AuthenticationType _authentication;
        private EncryptionType _encryption;
        private RadioType _radio;
        private Wireless80211_ConfigurationOptions _options;

        public Wireless80211ConfigurationViewModel(DeviceConfiguration.Wireless80211ConfigurationProperties model)
        {
            Model = model;

            _ssid = model.Ssid;
            _authentication = model.Authentication;
            _encryption = model.Encryption;
            _radio = model.Radio;
            _options = model.Wireless80211Options;
        }

        /// <summary>
        /// The Wi-Fi profile properties being edited.
        /// </summary>
        public DeviceConfiguration.Wireless80211ConfigurationProperties Model { get; }

        public string Ssid
        {
            get => _ssid;
            set => SetProperty(ref _ssid, value);
        }

        public AuthenticationType Authentication
        {
            get => _authentication;
            set => SetProperty(ref _authentication, value);
        }

        public EncryptionType Encryption
        {
            get => _encryption;
            set => SetProperty(ref _encryption, value);
        }

        public RadioType Radio
        {
            get => _radio;
            set => SetProperty(ref _radio, value);
        }

        public Wireless80211_ConfigurationOptions Options
        {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        /// <summary>
        /// Copies the edited values to <see cref="Model"/>.
        /// </summary>
        public void ApplyToModel()
        {
            Model.Ssid = Ssid;
            Model.Authentication = Authentication;
            Model.Encryption = Encryption;
            Model.Radio = Radio;
            Model.Wireless80211Options = Options;
        }
    }
}
