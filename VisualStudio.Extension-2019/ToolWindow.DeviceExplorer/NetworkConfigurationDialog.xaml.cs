//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using Microsoft.VisualStudio.Package;
    using Microsoft.VisualStudio.PlatformUI;
    using nanoFramework.Tools.Debugger;
    using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Windows.Forms;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class NetworkConfigurationDialog : DialogWindow
    {
        private static readonly IPAddress EmptyIPAddress = new IPAddress(0x0);
        private static readonly IPAddress DefaultMaskIPv4 = new IPAddress(new byte[] { 255, 255, 255, 0 });

        private DeviceExplorerViewModel DeviceExplorerViewModel => DataContext as DeviceExplorerViewModel;

        public NetworkConfigurationDialog(string helpTopic) : base(helpTopic)
        {
            InitializeComponent();
            InitControls();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkConfigurationDialog"/> class.
        /// </summary>
        public NetworkConfigurationDialog()
        {
            InitializeComponent();
            InitControls();
        }

        // init controls
        private void InitControls()
        {
            var networkConfiguration = DeviceExplorerViewModel.DeviceNetworkConfiguration;

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
            WiFiPassword.Password = wifiProfile?.Password;

            // if there is no valid network interface in the device: enable control for interface type selection
            if (DeviceExplorerViewModel.DeviceNetworkConfiguration.IsUnknown)
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
            Close();
        }

        private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // setup device network configuration block to save
            var networkConfigurationToSave = DeviceExplorerViewModel.DeviceNetworkConfiguration;

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
            DeviceExplorerViewModel.DeviceWireless80211Configuration.Password = WiFiPassword.Password;

            MessageCentre.StartProgressMessage($"Uploading network configuration to {DeviceExplorerViewModel.SelectedDevice.Description}...");

            // check if debugger engine exists
            if (DeviceExplorerViewModel.SelectedDevice.DebugEngine == null)
            {
                DeviceExplorerViewModel.SelectedDevice.CreateDebugEngine();
            }

            // save network configuration to target
            var updateResult = DeviceExplorerViewModel.SelectedDevice.DebugEngine.UpdateDeviceConfiguration(networkConfigurationToSave, 0);

            if (updateResult == Engine.UpdateDeviceResult.Sucess)
            {
                if (DeviceExplorerViewModel.DeviceNetworkConfiguration.InterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    // save Wi-Fi profile to target
                    updateResult = DeviceExplorerViewModel.SelectedDevice.DebugEngine.UpdateDeviceConfiguration(DeviceExplorerViewModel.DeviceWireless80211Configuration, 0);
                }
            }

            if (updateResult != Engine.UpdateDeviceResult.Sucess)
            {
                // update failed
                MessageCentre.OutputMessage($"Error updating {DeviceExplorerViewModel.SelectedDevice.Description} network configuration. Error: {updateResult}.");
                MessageCentre.StopProgressMessage();

                return;
            }
            else
            {
                // update of network config successful
                MessageCentre.OutputMessage($"{DeviceExplorerViewModel.SelectedDevice.Description} network configuration updated.");

                // is there a CA certificate bundle to upload?
                if (DeviceExplorerViewModel.CaCertificateBundle != null)
                {
                    MessageCentre.StartProgressMessage($"Uploading Root CA file to {(DataContext as DeviceExplorerViewModel).SelectedDevice.Description}...");

                    // save Root CA file to target
                    // at position 0
                    updateResult = DeviceExplorerViewModel.SelectedDevice.DebugEngine.UpdateDeviceConfiguration(DeviceExplorerViewModel.CaCertificateBundle, 0);

                    if (updateResult != Engine.UpdateDeviceResult.Sucess)
                    {
                        MessageCentre.OutputMessage($"Error uploading Root CA file to {(DataContext as DeviceExplorerViewModel).SelectedDevice.Description}. Error: {updateResult}");
                        MessageCentre.StopProgressMessage();

                        return;
                    }
                }

                // is there a device certificate to upload?
                if (DeviceExplorerViewModel.DeviceCertificate != null)
                {
                    MessageCentre.StartProgressMessage($"Uploading device certificate file to {(DataContext as DeviceExplorerViewModel).SelectedDevice.Description}...");

                    // save device certificate file to target
                    // at position 0

                    updateResult = DeviceExplorerViewModel.SelectedDevice.DebugEngine.UpdateDeviceConfiguration(DeviceExplorerViewModel.DeviceCertificate, 0);

                    if (updateResult != Engine.UpdateDeviceResult.Sucess)
                    {
                        MessageCentre.OutputMessage($"Error uploading device certificate file to {(DataContext as DeviceExplorerViewModel).SelectedDevice.Description}. Error: {updateResult}");
                        MessageCentre.StopProgressMessage();

                        return;
                    }
                }
            }

            // stop progress message
            MessageCentre.StopProgressMessage();

            // close on success
            Close();
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
            // store empty CA certificate
            DeviceExplorerViewModel.CaCertificateBundle = new DeviceConfiguration.X509CaRootBundleProperties()
            {
                Certificate = new byte[0],
                CertificateSize = 0
            };
        }

        private void ShowShowDeviceCertificatePicker_Click(object sender, System.Windows.RoutedEventArgs e)
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
            // store empty device certificate
            DeviceExplorerViewModel.DeviceCertificate = new DeviceConfiguration.X509DeviceCertificatesProperties()
            {
                Certificate = new byte[0],
                CertificateSize = 0
            };
        }
    }
}
