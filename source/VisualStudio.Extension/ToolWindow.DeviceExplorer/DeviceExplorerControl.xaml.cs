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
                (this.DataContext as DeviceExplorerViewModel).SelectedDevice = null;
                return;
            }

            // sanity check for no device in tree view
            if ((sender as TreeView).Items.Count > 0)
            {
                (this.DataContext as DeviceExplorerViewModel).SelectedDevice = (NanoDeviceBase)e.NewValue;
            }
        }
    }
}
