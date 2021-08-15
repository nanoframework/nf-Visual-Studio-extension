//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using Microsoft.VisualStudio.PlatformUI;
    using System.Collections.Generic;
    using System.Net;
    using System.Windows.Controls.Primitives;
    using System.Windows.Forms;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Interaction logic for DeviceExplorerControl.
    /// </summary>
    public partial class SettingsDialog : DialogWindow
    {
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
            // set controls according to stored preferences
            GenerateDeploymentImage.IsChecked = NanoFrameworkPackage.SettingGenerateDeploymentImage;
            IncludeConfigBlock.IsChecked = NanoFrameworkPackage.SettingIncludeConfigBlockInDeploymentImage;
            AutoUpdateEnable.IsChecked = NanoFrameworkPackage.SettingAutoUpdateEnable;
            IncludePrereleaseUpdates.IsChecked = NanoFrameworkPackage.SettingIncludePrereleaseUpdates;
            EnableVirtualDevice.IsChecked = NanoFrameworkPackage.SettingVirtualDeviceEnable;
            PortName.Text = NanoFrameworkPackage.SettingVirtualDevicePort;
            CLICommandPath.Text = NanoFrameworkPackage.SettingVirtualDeviceCLIPath;
            ShowInCommandWindow.IsChecked = NanoFrameworkPackage.SettingVirtualDeviceCommandWindow;

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

            // OK to add event handlers to controls now
            StoreCacheToUserPath.Checked += StoreCacheLocationChanged_Checked;
            StoreCacheToUserPath.Unchecked += StoreCacheLocationChanged_Checked;

            if (virtualDeviceProcess == null)
            {
                StartStopDevice.Content = "Start virtual device";
            }
            else
            {
                StartStopDevice.Content = "Stop virtual device";
            }


            // set focus on close button
            CloseButton.Focus();
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
            if(StoreCacheToProjectOutputPath.IsChecked.GetValueOrDefault())
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

        private void EnableVirtualDevice_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            NanoFrameworkPackage.SettingVirtualDeviceEnable = (sender as ToggleButton).IsChecked ?? false; 

        }

        private void PortName_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            NanoFrameworkPackage.SettingVirtualDevicePort = PortName.Text;
        }

        private void CLICommandPath_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            NanoFrameworkPackage.SettingVirtualDeviceCLIPath = CLICommandPath.Text;
        }

        private void CLIFilePicker_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.ShowNewFolderButton = true;

            // let's try make things easier:
            // if the current path is set, open folder browser dialog with it
            if (!string.IsNullOrEmpty(CLICommandPath.Text))
            {
                folderBrowserDialog.SelectedPath = CLICommandPath.Text;
            }

            // show dialog
            DialogResult result = folderBrowserDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
            {
                // looks like we have a valid path
                // save setting
                NanoFrameworkPackage.SettingVirtualDeviceCLIPath = folderBrowserDialog.SelectedPath;
                // update UI control too
                CLICommandPath.Text = folderBrowserDialog.SelectedPath;
            }
            else
            {
                // any other outcome from folder browser dialog doesn't require processing
            }

        }

        static Process virtualDeviceProcess = null;
        private void StartStopDevice_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            bool started = false;
            if (virtualDeviceProcess == null)
            {
                try
                {
                    StartStopDevice.Content = "Stop virtual device";

                    virtualDeviceProcess = new Process();

                    virtualDeviceProcess.StartInfo.UseShellExecute = false; 
                    virtualDeviceProcess.StartInfo.CreateNoWindow = !NanoFrameworkPackage.SettingVirtualDeviceCommandWindow;
                    virtualDeviceProcess.StartInfo.FileName = Path.Combine(NanoFrameworkPackage.SettingVirtualDeviceCLIPath, "nanoFramework.nanoCLR.CLI.exe");
                    if (File.Exists(virtualDeviceProcess.StartInfo.FileName) == false)
                    {
                        MessageBox.Show($"Invalid path to virtual device CLI: {virtualDeviceProcess.StartInfo.FileName}");
                        virtualDeviceProcess = null;
                        StartStopDevice.Content = "Start virtual device";
                        return;
                    }

                    virtualDeviceProcess.StartInfo.Arguments = $"run --serialport {NanoFrameworkPackage.SettingVirtualDevicePort}";
                    started = virtualDeviceProcess.Start();
                    virtualDeviceProcess.WaitForExit(1000);  // give it one second to start up and possibly exit with error
                    if (virtualDeviceProcess.HasExited)
                    {
                        MessageBox.Show("Failed to start the virtual device.");
                        virtualDeviceProcess = null;
                        StartStopDevice.Content = "Start virtual device";
                    }

                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Failed to start virtual device with error: {ex.Message}");
                    virtualDeviceProcess = null;
                    StartStopDevice.Content = "Start virtual device";
                }
            }
            else
            {
                // if the process is in use, kill it
                if (virtualDeviceProcess != null)
                {
                    try
                    {
                        virtualDeviceProcess.Kill();
                    }
                    catch (System.Exception)
                    {

                    }
                    virtualDeviceProcess = null;
                    StartStopDevice.Content = "Start virtual device";
                }
            }

        }

        private void ShowInCommandWindow_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            NanoFrameworkPackage.SettingVirtualDeviceCommandWindow = (sender as ToggleButton).IsChecked ?? false;

        }
    }
}
