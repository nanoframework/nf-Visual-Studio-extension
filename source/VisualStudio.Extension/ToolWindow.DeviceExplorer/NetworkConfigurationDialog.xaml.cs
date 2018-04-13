//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using GalaSoft.MvvmLight.Messaging;
    using Microsoft.Practices.ServiceLocation;
    using Microsoft.VisualStudio.PlatformUI;
    using Microsoft.VisualStudio.Shell;
    using nanoFramework.Tools.Debugger;
    using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
    using System;
    using System.Net;
    using System.Linq;
    using System.Windows.Controls;
    using System.Windows.Threading;
    using System.Net.NetworkInformation;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class NetworkConfigurationDialog : DialogWindow
    {
        private static IPAddress _InvalidIPv4 = new IPAddress(0x0);

        // strongly-typed view models enable x:bind
        public DeviceExplorerViewModel ViewModel => DataContext as DeviceExplorerViewModel;

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
            var networkConfiguration = (DataContext as DeviceExplorerViewModel).DeviceNetworkConfiguration;

            // set IPv4 addresses
            // DHCP ?
            if ((networkConfiguration.StartupAddressMode == AddressMode.DHCP) ||
                (networkConfiguration.StartupAddressMode == AddressMode.Invalid))
            {
                IPv4Automatic.IsChecked = true;
            }
            else
            {
                IPv4Manual.IsChecked = true;

                IPv4Address.SetAddress(networkConfiguration.IPv4Address);
                IPv4NetMask.SetAddress(networkConfiguration.IPv4NetMask);
                IPv4GatewayAddress.SetAddress(networkConfiguration.IPv4GatewayAddress);
            }

            // DNS auto?
            if((networkConfiguration.IPv4DNSAddress1.Equals(IPAddress.None) && networkConfiguration.IPv4DNSAddress2.Equals(IPAddress.None)) ||
                (networkConfiguration.IPv4DNSAddress1.Equals(_InvalidIPv4) && networkConfiguration.IPv4DNSAddress2.Equals(_InvalidIPv4)))
            {
                IPv4DnsAutomatic.IsChecked = true;
            }
            else
            {
                IPv4DnsManual.IsChecked = true;

                IPv4Dns1Address.SetAddress(networkConfiguration.IPv4DNSAddress1);
                IPv4Dns2Address.SetAddress(networkConfiguration.IPv4DNSAddress2);
            }

            // MAC address
            MACAddress.Text = String.Join("", networkConfiguration.MacAddress.Select(a => a.ToString("X2")));
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // setup device network configuration block to save
            var networkConfigurationToSave = (DataContext as DeviceExplorerViewModel).DeviceNetworkConfiguration;

            // IPv4 address options
            if(IPv4Automatic.IsChecked.GetValueOrDefault())
            {
                // IPv4 from DHCP
                networkConfigurationToSave.StartupAddressMode = AddressMode.DHCP;

                // clear remaining options
                networkConfigurationToSave.IPv4Address = IPAddress.None;
                networkConfigurationToSave.IPv4NetMask = IPAddress.None;
                networkConfigurationToSave.IPv4GatewayAddress = IPAddress.None;
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
            if(IPv4DnsAutomatic.IsChecked.GetValueOrDefault())
            {
                // IPv4 DNS is provided by DHCP
                // clear DNS addresses
                networkConfigurationToSave.IPv4DNSAddress1 = IPAddress.None;
                networkConfigurationToSave.IPv4DNSAddress2 = IPAddress.None;
            }
            else
            {
                // IPv4 DNS is static
                networkConfigurationToSave.IPv4DNSAddress1 = IPv4Dns1Address.GetAddress();
                networkConfigurationToSave.IPv4DNSAddress2 = IPv4Dns2Address.GetAddress();
            }

            // IPv6 options are not being handled for now
            // FIXME
            networkConfigurationToSave.IPv6Address = IPAddress.None;
            networkConfigurationToSave.IPv6NetMask = IPAddress.None;
            networkConfigurationToSave.IPv6GatewayAddress = IPAddress.None;
            networkConfigurationToSave.IPv6DNSAddress1 = IPAddress.None;
            networkConfigurationToSave.IPv6DNSAddress2 = IPAddress.None;

            // update MAC address
            try
            {
                var newMACAddress = MACAddress.Text;
                var newMACAddressArray = newMACAddress.Split(':');

                networkConfigurationToSave.MacAddress = newMACAddressArray.Select(a => byte.Parse(a, System.Globalization.NumberStyles.HexNumber)).ToArray();
            }
            catch(Exception ex)
            {
                // error parsing MAC address field
                throw new Exception("Invalid MAC address format. Check value.");
            }

            NanoFrameworkPackage.MessageCentre.StartProgressMessage($"Uploading network configuration to {(DataContext as DeviceExplorerViewModel).SelectedDevice.Description}...");

            // save network configuration to target...
            if ((DataContext as DeviceExplorerViewModel).SelectedDevice.DebugEngine.UpdateDeviceConfiguration(networkConfigurationToSave, 0))
            {
                NanoFrameworkPackage.MessageCentre.DebugMessage($"{(DataContext as DeviceExplorerViewModel).SelectedDevice.Description} network configuration updated.");
                NanoFrameworkPackage.MessageCentre.StopProgressMessage();

                // close on success
                Close();
            }
            else
            {
                NanoFrameworkPackage.MessageCentre.DebugMessage($"Error updating {(DataContext as DeviceExplorerViewModel).SelectedDevice.Description} network configuration.");
                NanoFrameworkPackage.MessageCentre.StopProgressMessage();
            }
        }
    }
}
