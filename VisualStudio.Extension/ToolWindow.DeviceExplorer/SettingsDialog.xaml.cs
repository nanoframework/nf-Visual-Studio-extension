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
            List<string> comPortList = new List<string>();

            // need to wrap the processing in a try/catch to deal with bad user input/format
            try
            {
                // grab and parse COM port list
                if (!string.IsNullOrEmpty(PortBlackList.Text))
                {
                    comPortList.AddRange(PortBlackList.Text.Split(';'));
                }
            }
            catch
            { 
                // don't care about bad user input/format/etc 
            }

            NanoFrameworkPackage.NanoDeviceCommService.DebugClient.PortBlackList = comPortList;
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
    }
}
