//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// A Wi-Fi profile stored in the device, as listed and edited in the Wi-Fi tab of the network configuration dialog.
    /// </summary>
    /// <remarks>
    /// Profiles have no name: they are told apart by their slot (the index of the configuration block) and their SSID.
    /// The edits are kept here until the dialog saves them, <see cref="ToModel"/> builds the configuration block to write.
    /// </remarks>
    [DataContract]
    internal sealed class WifiProfileViewModel : NotifyPropertyChangedObject
    {
        // sizes of the fields in the configuration block (HAL_Configuration_Wireless80211)
        private const int SsidMaxLength = 32;
        private const int PasswordMaxLength = 63;

        private readonly DeviceConfiguration.Wireless80211ConfigurationProperties _model;

        private string _ssid;
        private string _password;
        private ChoiceItem _authentication;
        private ChoiceItem _encryption;
        private ChoiceItem _radio;
        private ChoiceItem _options;
        private bool _isPasswordVisible;
        private bool _isUsedAtStartup;
        private bool _hasDuplicateSsid;

        // values as read from the device, to tell if the profile was modified
        private string _savedValues;

        public WifiProfileViewModel(uint slot, DeviceConfiguration.Wireless80211ConfigurationProperties model)
        {
            _model = model;

            Slot = slot;

            // None is a valid choice for these, e.g. an open network
            AuthenticationOptions = ChoiceItem.ListFor(model.Authentication, includeZero: true);
            EncryptionOptions = ChoiceItem.ListFor(model.Encryption, includeZero: true);
            RadioOptions = ChoiceItem.ListFor(model.Radio);
            ConfigurationOptions = ChoiceItem.ListFor(model.Wireless80211Options, includeZero: true);

            _ssid = model.Ssid ?? string.Empty;
            _password = model.Password ?? string.Empty;
            _authentication = AuthenticationOptions.First(o => o.Value == (byte)model.Authentication);
            _encryption = EncryptionOptions.First(o => o.Value == (byte)model.Encryption);
            _radio = RadioOptions.First(o => o.Value == (byte)model.Radio);
            _options = ConfigurationOptions.First(o => o.Value == (byte)model.Wireless80211Options);

            _savedValues = CurrentValues;

            ToggleShowPasswordCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                IsPasswordVisible = !IsPasswordVisible;
                return Task.CompletedTask;
            });

            Validate();
        }

        /// <summary>
        /// Raised when a value of the profile changes.
        /// </summary>
        public event EventHandler Changed;

        /// <summary>
        /// The index of the configuration block where the profile is stored.
        /// </summary>
        public uint Slot { get; }

        /// <summary>
        /// The Id of the profile, the network interface uses the profile with its <c>SpecificConfigId</c>.
        /// </summary>
        public uint Id => _model.Id;

        /// <summary>
        /// The slot, as shown to the user (1-based).
        /// </summary>
        [DataMember]
        public string SlotText => (Slot + 1).ToString();

        [DataMember]
        public List<ChoiceItem> AuthenticationOptions { get; }

        [DataMember]
        public List<ChoiceItem> EncryptionOptions { get; }

        [DataMember]
        public List<ChoiceItem> RadioOptions { get; }

        [DataMember]
        public List<ChoiceItem> ConfigurationOptions { get; }

        [DataMember]
        public string Ssid
        {
            get => _ssid;
            set => SetValue(ref _ssid, value ?? string.Empty, nameof(Ssid));
        }

        [DataMember]
        public string Password
        {
            get => _password;
            set
            {
                if (SetValue(ref _password, value ?? string.Empty, nameof(Password)))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(MaskedPassword));
                }
            }
        }

        /// <summary>
        /// One bullet for each character of the password, shown over the password box while the password is hidden.
        /// </summary>
        [DataMember]
        public string MaskedPassword => new string('•', _password.Length);

        [DataMember]
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                if (SetProperty(ref _isPasswordVisible, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(IsPasswordHidden));
                }
            }
        }

        [DataMember]
        public bool IsPasswordHidden => !_isPasswordVisible;

        [DataMember]
        public AsyncCommand ToggleShowPasswordCommand { get; }

        [DataMember]
        public ChoiceItem Authentication
        {
            get => _authentication;
            set => SetValue(ref _authentication, value ?? _authentication, nameof(Authentication));
        }

        [DataMember]
        public ChoiceItem Encryption
        {
            get => _encryption;
            set => SetValue(ref _encryption, value ?? _encryption, nameof(Encryption));
        }

        [DataMember]
        public ChoiceItem Radio
        {
            get => _radio;
            set => SetValue(ref _radio, value ?? _radio, nameof(Radio));
        }

        [DataMember]
        public ChoiceItem Options
        {
            get => _options;
            set => SetValue(ref _options, value ?? _options, nameof(Options));
        }

        /// <summary>
        /// Line 1 of the profile in the list: the SSID.
        /// </summary>
        [DataMember]
        public string Title => string.IsNullOrEmpty(_ssid) ? "(no SSID)" : _ssid;

        /// <summary>
        /// Line 2 of the profile in the list: authentication, encryption and options.
        /// </summary>
        [DataMember]
        public string Summary => string.Join(
            " · ",
            new[] { _authentication.DisplayName, _encryption.DisplayName, _options.DisplayName }.Where(s => !string.IsNullOrEmpty(s)));

        /// <summary>
        /// The network interface connects with this profile when the device starts.
        /// </summary>
        [DataMember]
        public bool IsUsedAtStartup
        {
            get => _isUsedAtStartup;
            set => SetProperty(ref _isUsedAtStartup, value);
        }

        [DataMember]
        public bool IsModified => CurrentValues != _savedValues;

        /// <summary>
        /// Another profile has the same SSID: the user can't tell them apart by the SSID.
        /// </summary>
        [DataMember]
        public bool HasDuplicateSsid
        {
            get => _hasDuplicateSsid;
            set => SetProperty(ref _hasDuplicateSsid, value);
        }

        [DataMember]
        public string SsidError { get; private set; }

        [DataMember]
        public string PasswordError { get; private set; }

        [DataMember]
        public bool HasErrors => SsidError != null || PasswordError != null;

        /// <summary>
        /// Accessible name of the profile in the list.
        /// </summary>
        [DataMember]
        public string AutomationName => $"Slot {SlotText}, {Title}, {Summary}"
            + (IsUsedAtStartup ? ", used at startup" : string.Empty)
            + (IsModified ? ", modified" : string.Empty)
            + (HasErrors ? ", has errors" : string.Empty);

        private string CurrentValues => string.Join("\u0001", _ssid, _password, _authentication.Value, _encryption.Value, _radio.Value, _options.Value);

        /// <summary>
        /// Builds the configuration block to write to the device, with the edited values.
        /// </summary>
        public DeviceConfiguration.Wireless80211ConfigurationProperties ToModel()
        {
            return new DeviceConfiguration.Wireless80211ConfigurationProperties
            {
                Id = _model.Id,
                Ssid = _ssid,
                Password = _password,
                Authentication = (AuthenticationType)_authentication.Value,
                Encryption = (EncryptionType)_encryption.Value,
                Radio = (RadioType)_radio.Value,
                Wireless80211Options = (Wireless80211_ConfigurationOptions)_options.Value,
            };
        }

        /// <summary>
        /// The profile was written to the device: the current values become the saved ones.
        /// </summary>
        public void MarkSaved()
        {
            _savedValues = CurrentValues;

            Validate();

            RaiseNotifyPropertyChangedEvent(nameof(IsModified));
            RaiseNotifyPropertyChangedEvent(nameof(AutomationName));
        }

        public override string ToString() => $"{SlotText} · {Title}";

        private bool SetValue<T>(ref T field, T value, string propertyName)
        {
            if (!SetProperty(ref field, value, propertyName))
            {
                return false;
            }

            Validate();

            RaiseNotifyPropertyChangedEvent(nameof(Title));
            RaiseNotifyPropertyChangedEvent(nameof(Summary));
            RaiseNotifyPropertyChangedEvent(nameof(IsModified));
            RaiseNotifyPropertyChangedEvent(nameof(AutomationName));

            Changed?.Invoke(this, EventArgs.Empty);

            return true;
        }

        private void Validate()
        {
            // a profile is written only if it's modified, the values read from the device aren't questioned
            var isModified = IsModified;

            SsidError = isModified ? ValidateSsid(_ssid) : null;
            PasswordError = isModified ? ValidatePassword(_password, (AuthenticationType)_authentication.Value) : null;

            RaiseNotifyPropertyChangedEvent(nameof(SsidError));
            RaiseNotifyPropertyChangedEvent(nameof(PasswordError));
            RaiseNotifyPropertyChangedEvent(nameof(HasErrors));
        }

        private static string ValidateSsid(string ssid)
        {
            if (string.IsNullOrEmpty(ssid))
            {
                return "The SSID is required.";
            }

            // the debug library copies one byte per character to the configuration block
            if (Encoding.UTF8.GetByteCount(ssid) != ssid.Length)
            {
                return "Only ASCII characters are supported in the SSID.";
            }

            if (ssid.Length > SsidMaxLength)
            {
                return $"The SSID can't be longer than {SsidMaxLength} characters.";
            }

            return null;
        }

        private static string ValidatePassword(string password, AuthenticationType authentication)
        {
            if (Encoding.UTF8.GetByteCount(password) != password.Length)
            {
                return "Only ASCII characters are supported in the password.";
            }

            switch (authentication)
            {
                case AuthenticationType.WPA:
                case AuthenticationType.WPA2:
                    if (password.Length < 8 || password.Length > PasswordMaxLength)
                    {
                        return $"WPA and WPA2 passwords have 8 to {PasswordMaxLength} characters.";
                    }

                    break;

                case AuthenticationType.WEP:
                case AuthenticationType.Shared:
                    // 40 or 104 bit keys, as text or as hexadecimal digits
                    if (!(password.Length == 5
                        || password.Length == 13
                        || ((password.Length == 10 || password.Length == 26) && password.All(Uri.IsHexDigit))))
                    {
                        return "WEP keys have 5 or 13 characters, or 10 or 26 hexadecimal digits.";
                    }

                    break;

                default:
                    if (password.Length > PasswordMaxLength)
                    {
                        return $"The password can't be longer than {PasswordMaxLength} characters.";
                    }

                    break;
            }

            return null;
        }
    }
}
