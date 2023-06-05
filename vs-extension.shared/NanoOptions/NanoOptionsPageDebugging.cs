////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using GalaSoft.MvvmLight.Ioc;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System.ComponentModel;
using Commands = nanoFramework.Tools.Debugger.WireProtocol.Commands;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class NanoOptionsPageDebugging : DialogPage
    {
        [Category("General Output Settings")]
        [DisplayName("Process Stack Trace")]
        [Description("Determines if stack trace will be processed by the nano device. This is used to save processing time with crawling the stack and gatthering the details. It will also prevent detailed stack trace information to be sent to the debugger. The StackTrace property in the Exception will be empty.")]
        [DefaultValue(true)]
        public bool ProcessStackTraceOption { get; set; } = true;

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();

            try
            {
                // update execution conditions in nano device if there is a debug session active
                var device = SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().SelectedDevice;

                if (device?.DebugEngine != null
                    && device.DebugEngine.IsRunning)
                {
                    // update execution mode according to option
                    if (ProcessStackTraceOption)
                    {
                        _ = device.DebugEngine.SetExecutionMode(0, Commands.DebuggingExecutionChangeConditions.State.NoStackTraceInExceptions);
                    }
                    else
                    {
                        _ = device.DebugEngine.SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State.NoStackTraceInExceptions, 0);
                    }
                }
            }
            catch
            {
                // don't care about any exception here
                // it's OK to fail silently as we need to make sure that the settings are saved
            }
        }
    }
}
