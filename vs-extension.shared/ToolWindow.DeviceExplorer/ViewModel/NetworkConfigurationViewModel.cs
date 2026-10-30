//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.Debugger;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel
{
    /// <summary>
    /// Data context of the network configuration dialog: the network interface, its Wi-Fi profiles and the certificates of a device.
    /// </summary>
    /// <remarks>
    /// The edits are kept here and validated as they are typed. OK writes what changed to the device, through
    /// <see cref="DeviceOperations.ApplyNetworkConfiguration"/>, away from the UI thread. If a write fails the dialog stays open,
    /// with the changes, so the user can retry.
    /// </remarks>
    [DataContract]
    internal sealed class NetworkConfigurationViewModel : NotifyPropertyChangedObject
    {
        /// <summary>The dialog was closed with the title bar button, Esc or Cancel.</summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>The changes were saved.</summary>
        public const string OutcomeSaved = "saved";

        private const string DefaultNetMask = "255.255.255.0";

        private static readonly IPAddress EmptyIPAddress = new IPAddress(0x0);

        private static readonly string[] TabNames = { "ipv4", "network_interface", "wifi_profiles", "certificates" };

        private readonly DeviceConfiguration.NetworkConfigurationProperties _model;
        private readonly CancellationTokenSource _closeDialog = new CancellationTokenSource();
        private readonly SemaphoreSlim _saving = new SemaphoreSlim(1, 1);
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly SortedSet<string> _tabsViewed = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _actions = new SortedSet<string>(StringComparer.Ordinal);

        private int _selectedTabIndex;
        private bool _isIPv4Static;
        private string _ipv4Address;
        private string _ipv4NetMask;
        private string _ipv4Gateway;
        private bool _isDnsManual;
        private string _dns1Address;
        private string _dns2Address;
        private string _macAddress;
        private ChoiceItem _interfaceType;
        private WifiProfileViewModel _selectedProfile;
        private DeviceConfiguration.X509CaRootBundleProperties _rootCaToSave;
        private string _rootCaStatus;
        private DeviceConfiguration.X509DeviceCertificatesProperties _deviceCertificateToSave;
        private string _deviceCertificateStatus;
        private bool _isSaving;
        private string _statusText;

        // values as read from the device, to tell if the network configuration was modified
        private string _savedNetworkValues;

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkConfigurationViewModel"/> class.
        /// </summary>
        /// <param name="device">The device being configured. It's connected while the dialog is open.</param>
        /// <param name="networkConfiguration">The configuration of the network interface, or an empty one.</param>
        /// <param name="wirelessConfigurations">The Wi-Fi profiles stored in the device, in slot order.</param>
        /// <param name="canChangeMacAddress">The device allows changing its MAC address.</param>
        public NetworkConfigurationViewModel(
            NanoDeviceBase device,
            DeviceConfiguration.NetworkConfigurationProperties networkConfiguration,
            IList<DeviceConfiguration.Wireless80211ConfigurationProperties> wirelessConfigurations,
            bool canChangeMacAddress)
        {
            Device = device;
            _model = networkConfiguration;

            DeviceDescription = device.Description;
            CanChangeMacAddress = canChangeMacAddress;

            // the interface type can only be chosen if the device doesn't have a valid one
            CanChangeInterfaceType = networkConfiguration.IsUnknown || networkConfiguration.InterfaceType == NetworkInterfaceType.Unknown;
            InterfaceTypeOptions = ChoiceItem.ListFor(networkConfiguration.InterfaceType);
            _interfaceType = InterfaceTypeOptions.First(o => o.Value == (byte)networkConfiguration.InterfaceType);

            // IPv4 address: DHCP unless it's static
            _isIPv4Static = networkConfiguration.StartupAddressMode == AddressMode.Static;
            _ipv4Address = FormatAddress(networkConfiguration.IPv4Address);
            _ipv4NetMask = FormatAddress(networkConfiguration.IPv4NetMask) is string mask && mask.Length > 0 ? mask : DefaultNetMask;
            _ipv4Gateway = FormatAddress(networkConfiguration.IPv4GatewayAddress);

            // DNS: automatic unless it's set
            _isDnsManual = !(networkConfiguration.AutomaticDNS || networkConfiguration.IsUnknown);
            _dns1Address = FormatAddress(networkConfiguration.IPv4DNSAddress1);
            _dns2Address = FormatAddress(networkConfiguration.IPv4DNSAddress2);

            _macAddress = networkConfiguration.MacAddress is null
                ? string.Empty
                : string.Join(":", networkConfiguration.MacAddress.Select(b => b.ToString("X2")));

            _savedNetworkValues = NetworkValues;

            // Wi-Fi profiles, by slot
            if (wirelessConfigurations.Count == 0)
            {
                // nothing stored yet: start with an empty profile in the 1st slot, it's written only if it's edited
                wirelessConfigurations = new[]
                {
                    new DeviceConfiguration.Wireless80211ConfigurationProperties
                    {
                        Ssid = string.Empty,
                        Password = string.Empty,
                        Authentication = AuthenticationType.WPA2,
                        Encryption = EncryptionType.WPA2,
                        Radio = RadioType._802_11n,
                        Wireless80211Options = Wireless80211_ConfigurationOptions.AutoConnect,
                    }
                };
            }

            for (int slot = 0; slot < wirelessConfigurations.Count; slot++)
            {
                var profile = new WifiProfileViewModel((uint)slot, wirelessConfigurations[slot]);
                profile.Changed += (sender, e) => OnProfileChanged();

                WifiProfiles.Add(profile);
            }

            UpdateProfilesUsedAtStartup();
            UpdateDuplicateSsids();

            _selectedProfile = WifiProfiles.FirstOrDefault(p => p.IsUsedAtStartup) ?? WifiProfiles.FirstOrDefault();

            _rootCaStatus = CertificateUnchanged;
            _deviceCertificateStatus = CertificateUnchanged;

            OkCommand = new AsyncCommand((parameter, cancellationToken) => SaveAsync());
            CancelCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                Close(OutcomeCancelled);
                return Task.CompletedTask;
            });
            BrowseRootCaCommand = new AsyncCommand((parameter, cancellationToken) => BrowseRootCaAsync());
            ClearRootCaCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                _actions.Add("root_ca_cleared");

                // an empty certificate clears the one stored in the device
                SetRootCaToSave(
                    new DeviceConfiguration.X509CaRootBundleProperties { Certificate = new byte[0], CertificateSize = 0 },
                    "The Root CA stored in the device will be cleared.");

                return Task.CompletedTask;
            });
            KeepRootCaCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                SetRootCaToSave(null, CertificateUnchanged);
                return Task.CompletedTask;
            });
            BrowseDeviceCertificateCommand = new AsyncCommand((parameter, cancellationToken) => BrowseDeviceCertificateAsync());
            ClearDeviceCertificateCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                _actions.Add("device_cert_cleared");

                // an empty certificate clears the one stored in the device
                SetDeviceCertificateToSave(
                    new DeviceConfiguration.X509DeviceCertificatesProperties { Certificate = new byte[0], CertificateSize = 0 },
                    "The device certificate stored in the device will be cleared.");

                return Task.CompletedTask;
            });
            KeepDeviceCertificateCommand = new AsyncCommand((parameter, cancellationToken) =>
            {
                SetDeviceCertificateToSave(null, CertificateUnchanged);
                return Task.CompletedTask;
            });

            _tabsViewed.Add(TabNames[0]);

            Validate();
        }

        private static string CertificateUnchanged => "Not changed.";

        /// <summary>
        /// The device being configured.
        /// </summary>
        public NanoDeviceBase Device { get; }

        /// <summary>
        /// Cancelled to close the dialog, it's the cancellation token of the dialog.
        /// </summary>
        public CancellationToken CloseToken => _closeDialog.Token;

        /// <summary>
        /// Waits for a save that is running, e.g. if the dialog was closed with Esc while saving, so the device stays connected until it's done.
        /// </summary>
        public async Task WaitForSaveAsync()
        {
            await _saving.WaitAsync();

            _saving.Release();
        }

        /// <summary>
        /// How the dialog was closed, for telemetry.
        /// </summary>
        public string Outcome { get; private set; } = OutcomeCancelled;

        [DataMember]
        public string DeviceDescription { get; }

        /// <summary>
        /// The title of the dialog.
        /// </summary>
        public string DialogTitle => $"Network Configuration - {DeviceDescription}";

        [DataMember]
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value) && value >= 0 && value < TabNames.Length)
                {
                    _tabsViewed.Add(TabNames[value]);
                }
            }
        }

        #region IPv4

        [DataMember]
        public bool IsIPv4Static
        {
            get => _isIPv4Static;
            set => SetNetworkValue(ref _isIPv4Static, value, nameof(IsIPv4Static), nameof(IsIPv4Dhcp));
        }

        [DataMember]
        public bool IsIPv4Dhcp
        {
            get => !_isIPv4Static;
            set => IsIPv4Static = !value;
        }

        [DataMember]
        public string IPv4Address
        {
            get => _ipv4Address;
            set => SetNetworkValue(ref _ipv4Address, value?.Trim() ?? string.Empty, nameof(IPv4Address));
        }

        [DataMember]
        public string IPv4NetMask
        {
            get => _ipv4NetMask;
            set => SetNetworkValue(ref _ipv4NetMask, value?.Trim() ?? string.Empty, nameof(IPv4NetMask));
        }

        [DataMember]
        public string IPv4Gateway
        {
            get => _ipv4Gateway;
            set => SetNetworkValue(ref _ipv4Gateway, value?.Trim() ?? string.Empty, nameof(IPv4Gateway));
        }

        [DataMember]
        public bool IsDnsManual
        {
            get => _isDnsManual;
            set => SetNetworkValue(ref _isDnsManual, value, nameof(IsDnsManual), nameof(IsDnsAutomatic));
        }

        [DataMember]
        public bool IsDnsAutomatic
        {
            get => !_isDnsManual;
            set => IsDnsManual = !value;
        }

        [DataMember]
        public string Dns1Address
        {
            get => _dns1Address;
            set => SetNetworkValue(ref _dns1Address, value?.Trim() ?? string.Empty, nameof(Dns1Address));
        }

        [DataMember]
        public string Dns2Address
        {
            get => _dns2Address;
            set => SetNetworkValue(ref _dns2Address, value?.Trim() ?? string.Empty, nameof(Dns2Address));
        }

        [DataMember]
        public string IPv4AddressError { get; private set; }

        [DataMember]
        public string IPv4NetMaskError { get; private set; }

        [DataMember]
        public string IPv4GatewayError { get; private set; }

        [DataMember]
        public string Dns1AddressError { get; private set; }

        [DataMember]
        public string Dns2AddressError { get; private set; }

        #endregion

        #region network interface

        [DataMember]
        public bool CanChangeMacAddress { get; }

        [DataMember]
        public string MacAddress
        {
            get => _macAddress;
            set => SetNetworkValue(ref _macAddress, value?.Trim() ?? string.Empty, nameof(MacAddress));
        }

        [DataMember]
        public string MacAddressError { get; private set; }

        [DataMember]
        public bool CanChangeInterfaceType { get; }

        [DataMember]
        public List<ChoiceItem> InterfaceTypeOptions { get; }

        [DataMember]
        public ChoiceItem InterfaceType
        {
            get => _interfaceType;
            set
            {
                if (SetNetworkValue(ref _interfaceType, value ?? _interfaceType, nameof(InterfaceType)))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(IsWireless));
                }
            }
        }

        /// <summary>
        /// The network interface is Wi-Fi: the Wi-Fi profiles are edited and saved.
        /// </summary>
        [DataMember]
        public bool IsWireless => _interfaceType.Value == (byte)NetworkInterfaceType.Wireless80211;

        #endregion

        #region Wi-Fi profiles

        [DataMember]
        public ObservableList<WifiProfileViewModel> WifiProfiles { get; } = new ObservableList<WifiProfileViewModel>();

        [DataMember]
        public WifiProfileViewModel SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(HasSelectedProfile));
                }
            }
        }

        [DataMember]
        public bool HasSelectedProfile => _selectedProfile != null;

        /// <summary>
        /// Explains how many profiles the device has and which one it connects with.
        /// </summary>
        [DataMember]
        public string WifiProfilesSummary
        {
            get
            {
                var count = WifiProfiles.Count;
                var text = count == 1 ? "1 profile is stored in the device." : $"{count} profiles are stored in the device.";

                return WifiProfiles.Any(p => p.IsUsedAtStartup)
                    ? text + " The device connects with the one marked as used at startup, the others are available to applications."
                    : text;
            }
        }

        #endregion

        #region certificates

        [DataMember]
        public string RootCaStatus
        {
            get => _rootCaStatus;
            private set => SetProperty(ref _rootCaStatus, value);
        }

        [DataMember]
        public bool HasRootCaToSave => _rootCaToSave != null;

        [DataMember]
        public AsyncCommand BrowseRootCaCommand { get; }

        [DataMember]
        public AsyncCommand ClearRootCaCommand { get; }

        [DataMember]
        public AsyncCommand KeepRootCaCommand { get; }

        [DataMember]
        public string DeviceCertificateStatus
        {
            get => _deviceCertificateStatus;
            private set => SetProperty(ref _deviceCertificateStatus, value);
        }

        [DataMember]
        public bool HasDeviceCertificateToSave => _deviceCertificateToSave != null;

        [DataMember]
        public AsyncCommand BrowseDeviceCertificateCommand { get; }

        [DataMember]
        public AsyncCommand ClearDeviceCertificateCommand { get; }

        [DataMember]
        public AsyncCommand KeepDeviceCertificateCommand { get; }

        /// <summary>
        /// The Root CA bundle to upload, an empty one to clear it, or <see langword="null"/> to leave it.
        /// </summary>
        public DeviceConfiguration.X509CaRootBundleProperties RootCaToSave => _rootCaToSave;

        /// <summary>
        /// The device certificate to upload, an empty one to clear it, or <see langword="null"/> to leave it.
        /// </summary>
        public DeviceConfiguration.X509DeviceCertificatesProperties DeviceCertificateToSave => _deviceCertificateToSave;

        #endregion

        #region validation and saving

        /// <summary>
        /// A value can't be saved. OK is disabled.
        /// </summary>
        [DataMember]
        public bool HasErrors { get; private set; }

        /// <summary>
        /// Which tabs have values that can't be saved.
        /// </summary>
        [DataMember]
        public string ErrorSummary { get; private set; }

        [DataMember]
        public bool CanSave => !HasErrors && !_isSaving;

        [DataMember]
        public bool IsSaving
        {
            get => _isSaving;
            private set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(CanSave));
                    RaiseNotifyPropertyChangedEvent(nameof(IsIdle));
                }
            }
        }

        [DataMember]
        public bool IsIdle => !_isSaving;

        /// <summary>
        /// Progress of the save, or why it failed.
        /// </summary>
        [DataMember]
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (SetProperty(ref _statusText, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(HasStatusText));
                }
            }
        }

        [DataMember]
        public bool HasStatusText => !string.IsNullOrEmpty(_statusText);

        [DataMember]
        public AsyncCommand OkCommand { get; }

        [DataMember]
        public AsyncCommand CancelCommand { get; }

        /// <summary>
        /// The network interface configuration was edited.
        /// </summary>
        public bool IsNetworkModified => NetworkValues != _savedNetworkValues;

        /// <summary>
        /// The Wi-Fi profiles to write: the ones that were edited, if the network interface is Wi-Fi.
        /// </summary>
        public IList<WifiProfileViewModel> WifiProfilesToSave => IsWireless
            ? WifiProfiles.Where(p => p.IsModified).ToList()
            : new List<WifiProfileViewModel>();

        private string NetworkValues => string.Join(
            "\u0001",
            _isIPv4Static,
            _isIPv4Static ? _ipv4Address : string.Empty,
            _isIPv4Static ? _ipv4NetMask : string.Empty,
            _isIPv4Static ? _ipv4Gateway : string.Empty,
            _isDnsManual,
            _isDnsManual ? _dns1Address : string.Empty,
            _isDnsManual ? _dns2Address : string.Empty,
            _macAddress,
            _interfaceType.Value);

        /// <summary>
        /// Builds the network interface configuration block to write to the device, with the edited values.
        /// </summary>
        public DeviceConfiguration.NetworkConfigurationProperties BuildNetworkConfiguration()
        {
            var configuration = _model;

            configuration.InterfaceType = (NetworkInterfaceType)_interfaceType.Value;

            if (_isIPv4Static)
            {
                configuration.StartupAddressMode = AddressMode.Static;
                configuration.IPv4Address = ParseAddress(_ipv4Address);
                configuration.IPv4NetMask = ParseAddress(_ipv4NetMask);
                configuration.IPv4GatewayAddress = ParseAddress(_ipv4Gateway);
            }
            else
            {
                // IPv4 from DHCP, clear the static options
                configuration.StartupAddressMode = AddressMode.DHCP;
                configuration.IPv4Address = EmptyIPAddress;
                configuration.IPv4NetMask = IPAddress.Parse(DefaultNetMask);
                configuration.IPv4GatewayAddress = EmptyIPAddress;
            }

            if (_isDnsManual)
            {
                configuration.AutomaticDNS = false;
                configuration.IPv4DNSAddress1 = ParseAddress(_dns1Address);
                configuration.IPv4DNSAddress2 = ParseAddress(_dns2Address);
            }
            else
            {
                // DNS provided by the DHCP server
                configuration.AutomaticDNS = true;
                configuration.IPv4DNSAddress1 = EmptyIPAddress;
                configuration.IPv4DNSAddress2 = EmptyIPAddress;
            }

            // IPv6 isn't supported for now
            configuration.IPv6Address = EmptyIPAddress;
            configuration.IPv6NetMask = EmptyIPAddress;
            configuration.IPv6GatewayAddress = EmptyIPAddress;
            configuration.IPv6DNSAddress1 = EmptyIPAddress;
            configuration.IPv6DNSAddress2 = EmptyIPAddress;

            if (CanChangeMacAddress)
            {
                configuration.MacAddress = ParseMacAddress(_macAddress);
            }

            return configuration;
        }

        /// <summary>
        /// The network interface configuration was written to the device.
        /// </summary>
        public void MarkNetworkSaved() => _savedNetworkValues = NetworkValues;

        /// <summary>
        /// The certificates were written to the device.
        /// </summary>
        public void MarkCertificatesSaved()
        {
            SetRootCaToSave(null, CertificateUnchanged);
            SetDeviceCertificateToSave(null, CertificateUnchanged);
        }

        /// <summary>
        /// Records how the dialog was used, as a <c>DialogClosed</c> event. Only fixed names and counts, never what was typed.
        /// </summary>
        public void TrackClosed()
        {
            ExtensionTelemetry.TrackEvent("DialogClosed", new TelemetryProperties
            {
                { "dialog", "networkConfig" },
                { "outcome", Outcome },
                { "duration_s", (int)_stopwatch.Elapsed.TotalSeconds },
                { "tabs", string.Join(",", _tabsViewed) },
                { "tab_count", _tabsViewed.Count },
                { "actions", ExtensionTelemetry.ListValue(_actions) },
                { "wifi_profiles", WifiProfiles.Count },
            });
        }

        private async Task SaveAsync()
        {
            // one save at a time, OK can be clicked again before it's disabled
            if (!CanSave || !await _saving.WaitAsync(0))
            {
                return;
            }

            IsSaving = true;
            StatusText = $"Saving the configuration to {DeviceDescription}...";

            try
            {
                // the device is written away from the UI thread
                await TaskScheduler.Default;

                var error = DeviceOperations.ApplyNetworkConfiguration(this);

                if (error is null)
                {
                    StatusText = null;

                    Close(OutcomeSaved);
                }
                else
                {
                    // the dialog stays open, with the changes, so the user can retry
                    _actions.Add("save_failed");

                    StatusText = error;
                }
            }
            finally
            {
                IsSaving = false;

                _saving.Release();
            }
        }

        private void Close(string outcome)
        {
            if (_isSaving && outcome != OutcomeSaved)
            {
                // can't leave while the device is being written
                return;
            }

            Outcome = outcome;

            _closeDialog.Cancel();
        }

        private bool SetNetworkValue<T>(ref T field, T value, string propertyName, string relatedPropertyName = null)
        {
            if (!SetProperty(ref field, value, propertyName))
            {
                return false;
            }

            if (relatedPropertyName != null)
            {
                RaiseNotifyPropertyChangedEvent(relatedPropertyName);
            }

            Validate();

            return true;
        }

        private void OnProfileChanged()
        {
            UpdateDuplicateSsids();

            Validate();
        }

        private void UpdateProfilesUsedAtStartup()
        {
            var specificConfigId = _model.SpecificConfigId;

            foreach (var profile in WifiProfiles)
            {
                profile.IsUsedAtStartup = specificConfigId.HasValue && profile.Id == specificConfigId.Value;
            }

            RaiseNotifyPropertyChangedEvent(nameof(WifiProfilesSummary));
        }

        private void UpdateDuplicateSsids()
        {
            var duplicates = new HashSet<string>(
                WifiProfiles
                    .Where(p => !string.IsNullOrEmpty(p.Ssid))
                    .GroupBy(p => p.Ssid, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.Ordinal);

            foreach (var profile in WifiProfiles)
            {
                profile.HasDuplicateSsid = duplicates.Contains(profile.Ssid);
            }
        }

        private void Validate()
        {
            if (_isIPv4Static)
            {
                IPv4AddressError = ValidateAddress(_ipv4Address, "IP address", required: true);
                IPv4NetMaskError = ValidateNetMask(_ipv4NetMask);
                IPv4GatewayError = ValidateAddress(_ipv4Gateway, "default gateway", required: false);
            }
            else
            {
                IPv4AddressError = IPv4NetMaskError = IPv4GatewayError = null;
            }

            if (_isDnsManual)
            {
                Dns1AddressError = ValidateAddress(_dns1Address, "preferred DNS server", required: true);
                Dns2AddressError = ValidateAddress(_dns2Address, "alternate DNS server", required: false);
            }
            else
            {
                Dns1AddressError = Dns2AddressError = null;
            }

            MacAddressError = CanChangeMacAddress && ParseMacAddress(_macAddress) is null
                ? "Enter the MAC address as 6 hexadecimal bytes, e.g. 00:1A:2B:3C:4D:5E."
                : null;

            var tabsWithErrors = new List<string>();

            if (IPv4AddressError != null || IPv4NetMaskError != null || IPv4GatewayError != null || Dns1AddressError != null || Dns2AddressError != null)
            {
                tabsWithErrors.Add("IPv4");
            }

            if (MacAddressError != null)
            {
                tabsWithErrors.Add("Network Interface");
            }

            // only the profiles that are going to be written have to be valid
            if (IsWireless && WifiProfiles.Any(p => p.IsModified && p.HasErrors))
            {
                tabsWithErrors.Add("Wi-Fi profiles");
            }

            HasErrors = tabsWithErrors.Count > 0;
            ErrorSummary = HasErrors ? $"Correct the values in {string.Join(", ", tabsWithErrors)} to save." : null;

            RaiseNotifyPropertyChangedEvent(nameof(IPv4AddressError));
            RaiseNotifyPropertyChangedEvent(nameof(IPv4NetMaskError));
            RaiseNotifyPropertyChangedEvent(nameof(IPv4GatewayError));
            RaiseNotifyPropertyChangedEvent(nameof(Dns1AddressError));
            RaiseNotifyPropertyChangedEvent(nameof(Dns2AddressError));
            RaiseNotifyPropertyChangedEvent(nameof(MacAddressError));
            RaiseNotifyPropertyChangedEvent(nameof(HasErrors));
            RaiseNotifyPropertyChangedEvent(nameof(ErrorSummary));
            RaiseNotifyPropertyChangedEvent(nameof(CanSave));
        }

        private void SetRootCaToSave(DeviceConfiguration.X509CaRootBundleProperties certificate, string status)
        {
            _rootCaToSave = certificate;
            RootCaStatus = status;

            RaiseNotifyPropertyChangedEvent(nameof(HasRootCaToSave));
        }

        private void SetDeviceCertificateToSave(DeviceConfiguration.X509DeviceCertificatesProperties certificate, string status)
        {
            _deviceCertificateToSave = certificate;
            DeviceCertificateStatus = status;

            RaiseNotifyPropertyChangedEvent(nameof(HasDeviceCertificateToSave));
        }

        private async Task BrowseRootCaAsync()
        {
            var fileName = await PickCertificateFileAsync("Select the Root CA file to upload");

            if (fileName is null)
            {
                return;
            }

            try
            {
                MessageCentre.InternalErrorWriteLine($"Opening certificate file: {fileName}");

                var certificate = File.ReadAllBytes(fileName);

                SetRootCaToSave(
                    new DeviceConfiguration.X509CaRootBundleProperties { Certificate = certificate, CertificateSize = (uint)certificate.Length },
                    $"{Path.GetFileName(fileName)} will be uploaded.");

                _actions.Add("root_ca_selected");
            }
            catch (Exception ex)
            {
                RootCaStatus = $"Error reading the Root CA file: {ex.Message}";

                MessageCentre.InternalErrorWriteLine($"Error reading Root CA file: {ex.Message} \r\n {ex.StackTrace}");
            }
        }

        private async Task BrowseDeviceCertificateAsync()
        {
            var fileName = await PickCertificateFileAsync("Select the device certificate file to upload");

            if (fileName is null)
            {
                return;
            }

            try
            {
                MessageCentre.InternalErrorWriteLine($"Opening device certificate file: {fileName}");

                var certificate = File.ReadAllBytes(fileName);

                // Mbed TLS parses a PEM certificate only if it ends with a terminator (0x00)
                if (string.Equals(Path.GetExtension(fileName), ".pem", StringComparison.OrdinalIgnoreCase)
                    && (certificate.Length == 0 || certificate[certificate.Length - 1] != 0x00))
                {
                    Array.Resize(ref certificate, certificate.Length + 1);
                }

                SetDeviceCertificateToSave(
                    new DeviceConfiguration.X509DeviceCertificatesProperties { Certificate = certificate, CertificateSize = (uint)certificate.Length },
                    $"{Path.GetFileName(fileName)} will be uploaded.");

                _actions.Add("device_cert_selected");
            }
            catch (Exception ex)
            {
                DeviceCertificateStatus = $"Error reading the device certificate file: {ex.Message}";

                MessageCentre.InternalErrorWriteLine($"Error reading device certificate file: {ex.Message} \r\n {ex.StackTrace}");
            }
        }

        private static async Task<string> PickCertificateFileAsync(string title)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // without an owner the file dialog is owned by the active window, the network configuration dialog
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "Certificate Files (*.crt;*.pem;*.der)|*.crt;*.pem;*.der|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true,
            };

            var fileName = openFileDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(openFileDialog.FileName)
                ? openFileDialog.FileName
                : null;

            // the file is read away from the UI thread
            await TaskScheduler.Default;

            return fileName;
        }

        #endregion

        #region address parsing

        private static string FormatAddress(IPAddress address)
        {
            return address is null || address.Equals(EmptyIPAddress) ? string.Empty : address.ToString();
        }

        /// <summary>
        /// Parses an IPv4 address in dotted decimal notation (four numbers from 0 to 255), empty is 0.0.0.0.
        /// </summary>
        private static IPAddress ParseAddress(string text)
        {
            return string.IsNullOrEmpty(text) ? EmptyIPAddress : TryParseAddress(text);
        }

        private static IPAddress TryParseAddress(string text)
        {
            // IPAddress.TryParse also takes "1" or "1.2" as addresses, only the full notation is accepted here
            var parts = text.Split('.');

            if (parts.Length != 4)
            {
                return null;
            }

            var bytes = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                if (parts[i].Length == 0
                    || parts[i].Length > 3
                    || !parts[i].All(char.IsDigit)
                    || !byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
                {
                    return null;
                }
            }

            return new IPAddress(bytes);
        }

        private static string ValidateAddress(string text, string name, bool required)
        {
            if (string.IsNullOrEmpty(text))
            {
                return required ? $"The {name} is required." : null;
            }

            return TryParseAddress(text) is null ? $"Enter the {name} as four numbers from 0 to 255, e.g. 192.168.1.10." : null;
        }

        private static string ValidateNetMask(string text)
        {
            var error = ValidateAddress(text, "subnet mask", required: true);

            if (error != null)
            {
                return error;
            }

            // the bits that are set have to be contiguous, from the left
            var bytes = TryParseAddress(text).GetAddressBytes();
            uint mask = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);

            return mask == 0 || (~mask & (~mask + 1)) != 0 ? "This isn't a valid subnet mask, e.g. 255.255.255.0." : null;
        }

        private static byte[] ParseMacAddress(string text)
        {
            var parts = text.Split(':', '-');

            if (parts.Length != 6)
            {
                return null;
            }

            var bytes = new byte[6];

            for (int i = 0; i < 6; i++)
            {
                if (parts[i].Length != 2
                    || !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                {
                    return null;
                }
            }

            return bytes;
        }

        #endregion
    }
}
