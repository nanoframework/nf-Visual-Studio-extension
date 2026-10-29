//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Windows.Forms;
    using Microsoft.VisualStudio.Package;
    using Microsoft.VisualStudio.PlatformUI;
    using nanoFramework.Tools.Debugger;
    using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class NetworkConfigurationDialog : DialogWindow
    {
        private static readonly IPAddress EmptyIPAddress = new IPAddress(0x0);
        private static readonly IPAddress DefaultMaskIPv4 = new IPAddress(new byte[] { 255, 255, 255, 0 });

        private DialogTelemetry _telemetry;

        private DeviceExplorerViewModel DeviceExplorerViewModel => DataContext as DeviceExplorerViewModel;

        public NetworkConfigurationDialog(DeviceExplorerViewModel viewModel, string helpTopic) : base(helpTopic)
        {
            InitializeComponent();
            DialogIcon.Apply(this);

            DataContext = viewModel;

            InitControls();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkConfigurationDialog"/> class.
        /// </summary>
        /// <param name="viewModel">The Device Explorer view model, with the configurations to edit.</param>
        public NetworkConfigurationDialog(DeviceExplorerViewModel viewModel)
        {
            InitializeComponent();
            DialogIcon.Apply(this);

            DataContext = viewModel;

            InitControls();
        }

        // init controls
        private void InitControls()
        {
            _telemetry = DialogTelemetry.Start(this, "networkConfig", IPversionOptions);

            var networkConfiguration = DeviceExplorerViewModel.DeviceNetworkConfiguration.Model;

            // developer note
            // because our IPMaskedTextBox is missing the required properties and events to support
            // MVVM and binding we have to use the old fashioned way of set/get properties in code behind

            // network config
            // set IPv4 addresses
            // DHCP ?
            if ((networkConfiguration.StartupAddressMode == AddressMode.DHCP) ||
                (networkConfiguration.StartupAddressMode == AddressMode.Invalid))
            {
                IPv4Automatic.IsChecked = true;

                IPv4Address.SetAddress(EmptyIPAddress);
                IPv4NetMask.SetAddress(DefaultMaskIPv4);
                IPv4GatewayAddress.SetAddress(EmptyIPAddress);
            }
            else
            {
                IPv4Manual.IsChecked = true;

                IPv4Address.SetAddress(networkConfiguration.IPv4Address ?? EmptyIPAddress);
                IPv4NetMask.SetAddress(networkConfiguration.IPv4NetMask ?? DefaultMaskIPv4);
                IPv4GatewayAddress.SetAddress(networkConfiguration.IPv4GatewayAddress ?? EmptyIPAddress);
            }

            // DNS is automatic?
            if (networkConfiguration.AutomaticDNS || networkConfiguration.IsUnknown)
            {
                IPv4DnsAutomatic.IsChecked = true;

                IPv4Dns1Address.SetAddress(EmptyIPAddress);
                IPv4Dns2Address.SetAddress(EmptyIPAddress);
            }
            else
            {
                IPv4DnsManual.IsChecked = true;

                IPv4Dns1Address.SetAddress(networkConfiguration.IPv4DNSAddress1 ?? EmptyIPAddress);
                IPv4Dns2Address.SetAddress(networkConfiguration.IPv4DNSAddress2 ?? EmptyIPAddress);
            }

            // wireless configuration/properties
            // get view model property
            var wifiProfile = DeviceExplorerViewModel.DeviceWireless80211Configuration;

            // set pass field if it's available from the model
            WiFiPassword.Password = wifiProfile?.Model.Password;

            // if there is no valid network interface in the device: enable control for interface type selection
            if (DeviceExplorerViewModel.DeviceNetworkConfiguration.Model.IsUnknown)
            {
                InterfaceType.IsEnabled = true;
            }

            // clear CA root certificate
            DeviceExplorerViewModel.CaCertificateBundle = null;

            // clear device certificate
            DeviceExplorerViewModel.DeviceCertificate = null;

            // set focus on cancel button
            CancelButton.Focus();
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _telemetry.Outcome = DialogTelemetry.OutcomeCancelled;

            Close();
        }

        private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // copy the values edited through the bindings to the configuration blocks to save
            DeviceExplorerViewModel.DeviceNetworkConfiguration.ApplyToModel();
            DeviceExplorerViewModel.DeviceWireless80211Configuration.ApplyToModel();

            // setup device network configuration block to save
            var networkConfigurationToSave = DeviceExplorerViewModel.DeviceNetworkConfiguration.Model;

            // IPv4 address options
            if (IPv4Automatic.IsChecked.GetValueOrDefault())
            {
                // IPv4 from DHCP
                networkConfigurationToSave.StartupAddressMode = AddressMode.DHCP;

                // clear remaining options
                networkConfigurationToSave.IPv4Address = EmptyIPAddress;
                networkConfigurationToSave.IPv4NetMask = DefaultMaskIPv4;
                networkConfigurationToSave.IPv4GatewayAddress = EmptyIPAddress;
            }
            else
            {
                // IPv4 has static configuration
                networkConfigurationToSave.StartupAddressMode = AddressMode.Static;

                // clear remaining options
                networkConfigurationToSave.IPv4Address = IPv4Address.GetAddress();
                networkConfigurationToSave.IPv4NetMask = IPv4NetMask.GetAddress();
                networkConfigurationToSave.IPv4GatewayAddress = IPv4GatewayAddress.GetAddress();
            }

            // IPv4 DNS options
            if (IPv4DnsAutomatic.IsChecked.GetValueOrDefault())
            {
                // IPv4 DNS is automatic and provided by DHCP server
                networkConfigurationToSave.AutomaticDNS = true;

                // clear DNS addresses
                networkConfigurationToSave.IPv4DNSAddress1 = EmptyIPAddress;
                networkConfigurationToSave.IPv4DNSAddress2 = EmptyIPAddress;
            }
            else
            {
                // IPv4 DNS is static
                networkConfigurationToSave.AutomaticDNS = false;

                networkConfigurationToSave.IPv4DNSAddress1 = IPv4Dns1Address.GetAddress();
                networkConfigurationToSave.IPv4DNSAddress2 = IPv4Dns2Address.GetAddress();
            }

            // IPv6 options are not being handled for now
            // FIXME
            networkConfigurationToSave.IPv6Address = EmptyIPAddress;
            networkConfigurationToSave.IPv6NetMask = EmptyIPAddress;
            networkConfigurationToSave.IPv6GatewayAddress = EmptyIPAddress;
            networkConfigurationToSave.IPv6DNSAddress1 = EmptyIPAddress;
            networkConfigurationToSave.IPv6DNSAddress2 = EmptyIPAddress;

            // process MAC address, if that can be updated
            if (DeviceExplorerViewModel.CanChangeMacAddress)
            {
                // check MAC address
                try
                {
                    var newMACAddress = MACAddress.Text;
                    var newMACAddressArray = newMACAddress.Split(':');
                    var dummyMacAddress = newMACAddressArray.Select(a => byte.Parse(a, System.Globalization.NumberStyles.HexNumber)).ToArray();
                }
                catch (Exception)
                {
                    // error parsing MAC address field
                    throw new Exception("Invalid MAC address format. Check value.");
                }
            }

            // Wi-Fi config
            DeviceExplorerViewModel.DeviceWireless80211Configuration.Model.Password = WiFiPassword.Password;

            // upload the configuration while the device is still connected
            if (DeviceOperations.ApplyNetworkConfiguration(networkConfigurationToSave))
            {
                _telemetry.Outcome = DialogTelemetry.OutcomeSaved;

                // close on success
                Close();
            }
            else
            {
                // the dialog stays open, the outcome is recorded with the NetworkConfigSaved event
                _telemetry.AddAction("save_failed");
            }
        }

        private void ShowShowRootCAFilePicker_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Certificate Files (*.crt;*.pem;*.der)|*.crt;*.pem;*.der|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            // show dialog
            DialogResult result = openFileDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
            {
                // looks like we have a valid path

                DeviceConfiguration.X509CaRootBundleProperties rootCaFile = new DeviceConfiguration.X509CaRootBundleProperties();

                // read file
                try
                {
                    MessageCentre.InternalErrorWriteLine($"Opening certificate file: {openFileDialog.FileName}");

                    using (FileStream binFile = new FileStream(openFileDialog.FileName, FileMode.Open))
                    {
                        rootCaFile.Certificate = new byte[binFile.Length];
                        binFile.Read(rootCaFile.Certificate, 0, (int)binFile.Length);
                        rootCaFile.CertificateSize = (uint)binFile.Length;
                    }

                    // store CA certificate
                    DeviceExplorerViewModel.CaCertificateBundle = rootCaFile;

                    _telemetry.AddAction("root_ca_selected");
                }
                catch (Exception ex)
                {
                    MessageCentre.OutputMessage($"Error reading Root CA file: {ex.Message}");

                    MessageCentre.InternalErrorWriteLine($"Error reading Root CA file: {ex.Message} \r\n {ex.StackTrace}");
                }
            }
            else
            {
                // any other outcome from folder browser dialog doesn't require processing
            }
        }

        private void ClearRootCA_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _telemetry.AddAction("root_ca_cleared");

            // store empty CA certificate
            DeviceExplorerViewModel.CaCertificateBundle = new DeviceConfiguration.X509CaRootBundleProperties()
            {
                Certificate = new byte[0],
                CertificateSize = 0
            };
        }

        private void ShowDeviceCertificatePicker_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Certificate Files (*.crt;*.pem;*.der)|*.crt;*.pem;*.der|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            // show dialog
            DialogResult result = openFileDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
            {
                // looks like we have a valid path

                DeviceConfiguration.X509DeviceCertificatesProperties deviceCertificateFile = new DeviceConfiguration.X509DeviceCertificatesProperties();

                // read file
                try
                {
                    MessageCentre.InternalErrorWriteLine($"Opening device certificate file: {openFileDialog.FileName}");

                    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    // Requirement from Mbed TLS parser: if the file is a PEM file, need to make sure it ends with a terminator (0x00) //
                    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    var fileIsPem = FilePathUtilities.GetFileExtension(openFileDialog.FileName) == ".pem";

                    using (FileStream binFile = new FileStream(openFileDialog.FileName, FileMode.Open))
                    {
                        var certificateContent = new byte[binFile.Length];
                        binFile.Read(certificateContent, 0, (int)binFile.Length);

                        if (fileIsPem)
                        {
                            // check if last position it's a terminator
                            if (certificateContent[certificateContent.Length - 1] != 0x00)
                            {
                                // nope, add terminator
                                Array.Resize(ref certificateContent, certificateContent.Length + 1);
                                certificateContent[certificateContent.Length - 1] = 0x00;
                            }
                        }

                        deviceCertificateFile.Certificate = certificateContent;
                        deviceCertificateFile.CertificateSize = (uint)certificateContent.Length;
                    }

                    // store device certificate
                    DeviceExplorerViewModel.DeviceCertificate = deviceCertificateFile;

                    _telemetry.AddAction("device_cert_selected");
                }
                catch (Exception ex)
                {
                    MessageCentre.OutputMessage($"Error reading device certificate file: {ex.Message}");

                    MessageCentre.InternalErrorWriteLine($"Error reading device certificate file: {ex.Message} \r\n {ex.StackTrace}");
                }
            }
            else
            {
                // any other outcome from folder browser dialog doesn't require processing
            }
        }

        private void ClearDeviceCertificate_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _telemetry.AddAction("device_cert_cleared");

            // store empty device certificate
            DeviceExplorerViewModel.DeviceCertificate = new DeviceConfiguration.X509DeviceCertificatesProperties()
            {
                Certificate = new byte[0],
                CertificateSize = 0
            };
        }
    }
}
