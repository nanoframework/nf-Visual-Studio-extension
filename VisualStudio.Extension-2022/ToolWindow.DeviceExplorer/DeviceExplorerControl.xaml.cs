//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.VisualStudio.Shell;
    using nanoFramework.Tools.Debugger;
    using nanoFramework.Tools.VisualStudio.Extension.Messages;
    using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
    using System;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class DeviceExplorerControl : UserControl, IRecipient<ForceSelectionOfNanoDeviceMessage>
    {
        // strongly-typed view models enable x:bind
        public DeviceExplorerViewModel ViewModel => DataContext as DeviceExplorerViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceExplorerControl"/> class.
        /// </summary>
        public DeviceExplorerControl()
        {
            InitializeComponent();

            Loaded += DeviceExplorerControl_Loaded;

            deviceTreeView.SelectedItemChanged += DevicesTreeView_SelectedItemChanged;
            WeakReferenceMessenger.Default.Register<ForceSelectionOfNanoDeviceMessage>(this);
        }

        private void DeviceExplorerControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // update status of the control buttons that depend on stored user preferences
            DeviceExplorerCommand.UpdateShowInternalErrorsButton(NanoFrameworkPackage.OptionShowInternalErrors);
            DeviceExplorerCommand.UpdateDisableDeviceWatchersButton(NanoFrameworkPackage.OptionDisableDeviceWatchers);
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

        private void DevicesTreeView_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            // if user has selected the 'devices' TreeViewItem (collapsing the tree view...)
            if (e.NewValue.GetType().Equals(typeof(TreeViewItem)))
            {
                // clear selected device
                // can't select header as the selected device
                (DataContext as DeviceExplorerViewModel).SelectedDevice = null;
                return;
            }

            // sanity check for no device in tree view
            if ((sender as TreeView).Items.Count > 0)
            {
                (DataContext as DeviceExplorerViewModel).SelectedDevice = (NanoDeviceBase)e.NewValue;
            }
        }


        #region MVVM messaging handlers

        private async System.Threading.Tasks.Task ForceSelectionOfNanoDeviceHandlerAsync()
        {
            int tryCount = 2;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // make sure the item in the treeview is selected, in case the selected device was changed in the view model
            // check if it's the same so we don't switch 
            if ((deviceTreeView.SelectedItem != null
                && (DataContext as DeviceExplorerViewModel).SelectedDevice != null)
                && deviceTreeView.SelectedItem.GetType().IsSubclassOf(typeof(NanoDeviceBase))
                && ((NanoDeviceBase)deviceTreeView.SelectedItem).Description == (DataContext as DeviceExplorerViewModel).SelectedDevice.Description)
            {
                // nothing to do here
                return;
            }

            do
            {
                // select the device
                if (DevicesHeaderItem.ItemContainerGenerator.ContainerFromItem((DataContext as DeviceExplorerViewModel).SelectedDevice) is TreeViewItem deviceItem)
                {
                    // switch to UI main thread
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // need to disable the event handler otherwise it will mess the selection
                    deviceTreeView.SelectedItemChanged -= DevicesTreeView_SelectedItemChanged;

                    if (deviceItem != null)
                    {
                        deviceItem.IsSelected = true;
                    }

                    // enabled it back
                    deviceTreeView.SelectedItemChanged += DevicesTreeView_SelectedItemChanged;
                }
                else
                {
                    // needs some time to allow the collection to be populated
                    await System.Threading.Tasks.Task.Delay(10).ConfigureAwait(true);
                }
            }
            while (tryCount-- > 0);

            WeakReferenceMessenger.Default.Send(new SelectedNanoDeviceHasChangedMessage());

            // force redrawing to show selection
            deviceTreeView.InvalidateVisual();
            deviceTreeView.UpdateLayout();
            deviceTreeView.InvalidateVisual();
        }


        public void Receive(ForceSelectionOfNanoDeviceMessage message)
        {
            ForceSelectionOfNanoDeviceHandlerAsync().ConfigureAwait(false);
        }
        #endregion
    }
}
