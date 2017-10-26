//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using GalaSoft.MvvmLight.Messaging;
    using Microsoft.VisualStudio.Shell;
    using nanoFramework.Tools.Debugger;
    using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
    using System;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class DeviceExplorerControl : UserControl
    {
        // strongly-typed view models enable x:bind
        public DeviceExplorerViewModel ViewModel => this.DataContext as DeviceExplorerViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceExplorerControl"/> class.
        /// </summary>
        public DeviceExplorerControl()
        {
            this.InitializeComponent();

            Messenger.Default.Register<NotificationMessage>(this, DeviceExplorerViewModel.MessagingTokens.ForceSelectionOfNanoDevice, (message) => ForceSelectionOfNanoDeviceHandler());
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            NavigateHyperlink(e.Uri);
            e.Handled = true;
        }

        private void NavigateHyperlink(Uri uri)
        {
            string page = uri.AbsoluteUri;
            VsShellUtilities.OpenSystemBrowser(page);

            // TODO: add telemetry for clicks 
        }

        private void TreeView_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {

        }

        private void DevicesTreeView_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            // if user has selected the 'devices' TreeViewItem (colapsing the tree view...)
            if (e.NewValue.GetType().Equals(typeof(TreeViewItem)))
            {
                // clear selected device
                // can't select header as the selected device
                (this.DataContext as DeviceExplorerViewModel).SelectedDevice = null;
                return;
            }

            // sanity check for no device in tree view
            if ((sender as TreeView).Items.Count > 0)
            {
                (this.DataContext as DeviceExplorerViewModel).SelectedDevice = (NanoDeviceBase)e.NewValue;
            }
        }


        #region MVVM messaging handlers

        private void ForceSelectionOfNanoDeviceHandler()
        {
            // make sure the item in the treeview is selected, in case the selected device was changed in the view model
            if(deviceTreeView.SelectedItem != null)
            {
                if(deviceTreeView.SelectedItem.GetType().Equals(typeof(NanoDeviceBase)))
                {
                    // check if it's the same so we don't switch 
                    if(((NanoDeviceBase)deviceTreeView.SelectedItem).Description == (this.DataContext as DeviceExplorerViewModel).SelectedDevice.Description)
                    {
                        // nothing to do here
                        return;
                    }
                }
            }

            // select the device
            var deviceItem = DevicesHeaderItem.ItemContainerGenerator.ContainerFromItem((this.DataContext as DeviceExplorerViewModel).SelectedDevice) as TreeViewItem;
            if (deviceItem != null)
            {
                deviceItem.IsSelected = true;
            }
        }

        #endregion
    }
}
