//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using GalaSoft.MvvmLight.Messaging;
    using Microsoft.VisualStudio.PlatformUI;
    using Microsoft.VisualStudio.Shell;
    using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
    using System.Collections.Generic;
    using System.Net;
    using System.Windows.Controls.Primitives;
    using System.Windows.Forms;
    using Task = System.Threading.Tasks.Task;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class SettingsDialog : DialogWindow
    {
        private const string _stopVirtualDeviceLabel = "Stop Virtual Device";
        private const string _startVirtualDeviceLabel = "Start Virtual Device";
        private static IPAddress _InvalidIPv4 = new IPAddress(0x0);

        public SettingsDialog(string helpTopic) : base(helpTopic)
        {
            InitializeComponent();
            InitControls();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsDialog"/> class.
        /// </summary>
        public SettingsDialog()
        {
            InitializeComponent();
            InitControls();
        }

        // init controls
        private void InitControls()
        {
            Messenger.Default.Register<NotificationMessage>(this, DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting, (message) => this.UpdateStartStopAvailabilityAsync(message.Notification).ConfigureAwait(false));

            // set controls according to stored preferences
            GenerateDeploymentImage.IsChecked = NanoFrameworkPackage.SettingGenerateDeploymentImage;
            IncludeConfigBlock.IsChecked = NanoFrameworkPackage.SettingIncludeConfigBlockInDeploymentImage;
            AutoUpdateEnable.IsChecked = NanoFrameworkPackage.SettingAutoUpdateEnable;
            IncludePrereleaseUpdates.IsChecked = NanoFrameworkPackage.SettingIncludePrereleaseUpdates;
            EnableVirtualDevice.IsChecked = NanoFrameworkPackage.SettingVirtualDeviceEnable;
            VirtualDeviceSerialPort.Text = NanoFrameworkPackage.SettingVirtualDevicePort;
            LoadNanoClrInstance.IsChecked = NanoFrameworkPackage.SettingLoadNanoClrInstance;
            PathOfLocalNanoClrInstance.Text = NanoFrameworkPackage.SettingPathOfLocalNanoClrInstance;
            AutoUpdateNanoClrImage.IsChecked = NanoFrameworkPackage.SettingVirtualDeviceAutoUpdateNanoClrImage;

            if (string.IsNullOrEmpty(NanoFrameworkPackage.SettingPathOfFlashDumpCache))
            {
                // no cache location specified
                StoreCacheToProjectOutputPath.IsChecked = true;
            }
            else
            {
                // user has a path in preferences
                StoreCacheToUserPath.IsChecked = true;
                PathOfFlashDumpCache.Text = NanoFrameworkPackage.SettingPathOfFlashDumpCache;
            }

            PortBlackList.Text = NanoFrameworkPackage.SettingPortBlackList;

            // update button content for start/stop device depending if it's running or not
            bool canStartStopVirtualDevice = NanoFrameworkPackage.VirtualDeviceService.CanStartStopVirtualDevice;
            bool virtualDeviceIsRunning = NanoFrameworkPackage.VirtualDeviceService.VirtualDeviceIsRunning;

            if (EnableVirtualDevice.IsChecked.Value
               && virtualDeviceIsRunning)
            {
                StartStopDevice.Content = _stopVirtualDeviceLabel;
            }

            // enable start/stop button if possible
            StartStopDevice.IsEnabled = canStartStopVirtualDevice;

            LoadNanoClrInstance.IsEnabled = EnableVirtualDevice.IsChecked.Value;
            ShowFilePickerLocalNanoClrInstance.IsEnabled = LoadNanoClrInstance.IsEnabled;

            if (!string.IsNullOrEmpty(VirtualDeviceSerialPort.Text))
            {
                // if there is a COM port set, enable the text box only if it's not running
                VirtualDeviceSerialPort.IsEnabled = !virtualDeviceIsRunning;
            }

            // OK to add event handlers to controls now
            StoreCacheToUserPath.Checked += StoreCacheLocationChanged_Checked;
            StoreCacheToUserPath.Unchecked += StoreCacheLocationChanged_Checked;
            VirtualDeviceSerialPort.LostFocus += VirtualDeviceSerialPort_LostFocus;
            EnableVirtualDevice.Checked += EnableVirtualSerialDevice_Checked;
            EnableVirtualDevice.Unchecked += EnableVirtualSerialDevice_Checked;
            LoadNanoClrInstance.Checked += LoadNanoClrInstance_Checked;
            LoadNanoClrInstance.Unchecked += LoadNanoClrInstance_Checked;

            // set focus on close button
            CloseButton.Focus();
        }

        private async Task UpdateStartStopAvailabilityAsync(string installCompleted)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            StartStopDevice.IsEnabled = !bool.Parse(installCompleted)
                                        && NanoFrameworkPackage.VirtualDeviceService.NanoClrIsInstalled
                                        && NanoFrameworkPackage.VirtualDeviceService.CanStartStopVirtualDevice;
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        private void GenerateDeploymentImage_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingGenerateDeploymentImage = (sender as ToggleButton).IsChecked ?? false;
        }

        private void IncludeConfigBlock_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingIncludeConfigBlockInDeploymentImage = (sender as ToggleButton).IsChecked ?? false;
        }

        private void StoreCacheLocationChanged_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (StoreCacheToProjectOutputPath.IsChecked.GetValueOrDefault())
            {
                // cache location is project output
                // save setting by clearing the path
                NanoFrameworkPackage.SettingPathOfFlashDumpCache = "";
                // clear UI control too
                PathOfFlashDumpCache.Text = "";
            }
            else
            {
                NanoFrameworkPackage.SettingPathOfFlashDumpCache = PathOfFlashDumpCache.Text;
            }
        }

        private void ShowFilePicker_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.ShowNewFolderButton = true;

            // let's try make things easier:
            // if the current path is set, open folder browser dialog with it
            if (!string.IsNullOrEmpty(PathOfFlashDumpCache.Text))
            {
                folderBrowserDialog.SelectedPath = PathOfFlashDumpCache.Text;
            }

            // show dialog
            DialogResult result = folderBrowserDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
            {
                // looks like we have a valid path
                // save setting
                NanoFrameworkPackage.SettingPathOfFlashDumpCache = folderBrowserDialog.SelectedPath;
                // update UI control too
                PathOfFlashDumpCache.Text = folderBrowserDialog.SelectedPath;
            }
            else
            {
                // any other outcome from folder browser dialog doesn't require processing
            }
        }

        private void PortBlackList_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // store updated setting
            NanoFrameworkPackage.SettingPortBlackList = PortBlackList.Text;

            // update debug client
            List<string> PortList = new List<string>();

            // need to wrap the processing in a try/catch to deal with bad user input/format
            try
            {
                // grab and parse COM port list
                if (!string.IsNullOrEmpty(PortBlackList.Text))
                {
                    PortList.AddRange(PortBlackList.Text.Split(';'));
                }
            }
            catch
            {
                // don't care about bad user input/format/etc 
            }

            NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortExclusionList = PortList;
        }

        private void AutoUpdateEnable_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingAutoUpdateEnable = (sender as ToggleButton).IsChecked ?? false;
        }

        private void IncludePrereleaseUpdates_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingIncludePrereleaseUpdates = (sender as ToggleButton).IsChecked ?? false;
        }

        private async void EnableVirtualSerialDevice_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingVirtualDeviceEnable = (sender as ToggleButton).IsChecked ?? false;

            LoadNanoClrInstance.IsEnabled = (sender as ToggleButton).IsChecked ?? false;
            ShowFilePickerLocalNanoClrInstance.IsEnabled = LoadNanoClrInstance.IsEnabled;

            // install/update nanoclr tool
            if (NanoFrameworkPackage.SettingVirtualDeviceEnable && !NanoFrameworkPackage.VirtualDeviceService.NanoClrIsInstalled)
            {
                // yield to give the UI thread a chance to respond to user input
                await Task.Yield();

                await Task.Run(() =>
                {
                    NanoFrameworkPackage.VirtualDeviceService.InstallNanoClrTool();
                });
            }
        }

        private void VirtualDeviceSerialPort_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // store updated setting
            NanoFrameworkPackage.SettingVirtualDevicePort = VirtualDeviceSerialPort.Text;
        }

        private async void StartStopDevice_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // yield to give the UI thread a chance to respond to user input
            await Task.Yield();

            await Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (NanoFrameworkPackage.VirtualDeviceService.VirtualDeviceIsRunning)
                {
                    NanoFrameworkPackage.VirtualDeviceService.StopVirtualDevice();

                    StartStopDevice.Content = _startVirtualDeviceLabel;

                    VirtualDeviceSerialPort.IsEnabled = true;
                }
                else
                {
                    if (await NanoFrameworkPackage.VirtualDeviceService.StartVirtualDeviceAsync(true))
                    {
                        StartStopDevice.Content = _stopVirtualDeviceLabel;
                        VirtualDeviceSerialPort.IsEnabled = false;

                        // if this is the 1st run, update serial port name, if not already there
                        if (string.IsNullOrEmpty(VirtualDeviceSerialPort.Text))
                        {
                            VirtualDeviceSerialPort.Text = NanoFrameworkPackage.SettingVirtualDevicePort;
                        }
                    }
                }
            });
        }

        private void AutoUpdateNanoClrImage_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingVirtualDeviceAutoUpdateNanoClrImage = (sender as ToggleButton).IsChecked ?? false;
        }

        private void LoadNanoClrInstance_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            // save new state
            NanoFrameworkPackage.SettingLoadNanoClrInstance = (sender as ToggleButton).IsChecked ?? false;

            // enable/disable file picker button
            ShowFilePickerLocalNanoClrInstance.IsEnabled = (sender as ToggleButton).IsChecked ?? false;
        }

        private void ShowFilePickerForNanoCLRInstance_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var filePickerDialog = new OpenFileDialog
            {
                Title = "Select nanoCLR instance",
                DefaultExt = ".dll",
                Filter = "nanoCLR instance (*.dll)|*.dll",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
            };

            // let's try make things easier:
            // if the current path is set, open folder browser dialog with it
            if (!string.IsNullOrEmpty(PathOfLocalNanoClrInstance.Text))
            {
                filePickerDialog.FileName = PathOfLocalNanoClrInstance.Text;
            }

            // show dialog
            DialogResult result = filePickerDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(filePickerDialog.FileName))
            {
                // looks like we have a valid path
                // save setting
                NanoFrameworkPackage.SettingPathOfLocalNanoClrInstance = filePickerDialog.FileName;
                // update UI control too
                PathOfLocalNanoClrInstance.Text = filePickerDialog.FileName;
            }
            else
            {
                // any other outcome from folder browser dialog doesn't require processing
            }
        }
    }
}
