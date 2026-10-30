////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using System.ComponentModel;
using Commands = nanoFramework.Tools.Debugger.WireProtocol.Commands;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Tools › Options › .NET nanoFramework › Debugging.
    /// </summary>
    public class NanoOptionsPageDebugging : NanoOptionsPage
    {
        [Category("General Output Settings")]
        [DisplayName("Process Stack Trace")]
        [Description("Determines if stack trace will be processed by the nano device. This is used to save processing time with crawling the stack and gatthering the details. It will also prevent detailed stack trace information to be sent to the debugger. The StackTrace property in the Exception will be empty.")]
        [DefaultValue(true)]
        public bool ProcessStackTraceOption { get; set; } = true;

        protected override void LoadSettings()
        {
            ProcessStackTraceOption = NanoSettings.ProcessStackTrace;
        }

        protected override void SaveSettings()
        {
            NanoSettings.ProcessStackTrace = ProcessStackTraceOption;
        }

        protected override void OnSettingsSaved()
        {
            // update execution conditions in nano device if there is a debug session active
            var device = NanoFrameworkPackage.DeviceExplorerViewModel?.SelectedDevice;

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
    }
}
