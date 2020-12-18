//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger;
using System;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class MessageCentre
    {
        protected static readonly Guid s_InternalErrorsPaneGuid = Guid.NewGuid();
        protected static readonly Guid s_DeploymentMessagesPaneGuid = Guid.NewGuid();
        protected static readonly Guid s_FirmwareUpdatManagerPane = Guid.NewGuid();

        private static IVsOutputWindow _outputWindow;
        private static IVsOutputWindowPane _debugPane;
        private static IVsOutputWindowPane _nanoFrameworkMessagesPane;
        private static IVsOutputWindowPane _firmwareUpdatManager;
        private static IVsStatusbar _statusBar;
        private static string _paneName;
        private static uint progressCookie;

        public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package, string name)
        {
            // seems OK to call these API here without switching to the main thread as we are just getting the service not actually accessing the output window
#pragma warning disable VSTHRD010
            _outputWindow = await package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Assumes.Present(_outputWindow);

            _statusBar = await package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            Assumes.Present(_statusBar);
#pragma warning restore VSTHRD010

            _paneName = name;

            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // get VS debug pane
                Guid tempId = VSConstants.GUID_OutWindowDebugPane;
                _outputWindow.GetPane(ref tempId, out _debugPane);

                // create nanoFramework pane
                tempId = s_DeploymentMessagesPaneGuid;
                _outputWindow.CreatePane(ref tempId, _paneName, 1, 0);
                _outputWindow.GetPane(ref tempId, out _nanoFrameworkMessagesPane);

                // create firmware update manager pane
                tempId = s_FirmwareUpdatManagerPane;
                _outputWindow.CreatePane(ref tempId, ".NET nanoFramework Firmware Update Manager", 1, 0);
                _outputWindow.GetPane(ref tempId, out _firmwareUpdatManager);
            });
        }

        /// <summary>
        /// Write a message to Visual Studio Debug output pane.
        /// </summary>
        /// <param name="message">Message to be outputted.</param>
        public static void DebugMessage(string message)
        {
            Message(_debugPane, message ?? "");
        }

        public static void ClearDeploymentMessages()
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _nanoFrameworkMessagesPane.Clear();
            });
        }

        public static void DeploymentMessage(string message)
        {
            Message(_nanoFrameworkMessagesPane, message);
        }

        public static void InternalErrorMessage(string message)
        {
            InternalErrorMessage(false, message);
        }

        public static void InternalErrorMessage(bool assertion, string message)
        {
            InternalErrorMessage(assertion, message, -1);
        }

        public static void InternalErrorMessage(bool assertion, string message, int skipFrames)
        {
            if (!assertion && NanoFrameworkPackage.OptionShowInternalErrors)
            {
                message = String.IsNullOrEmpty(message) ? "Unknown Error" : message;

                if (skipFrames >= 0)
                {
                    StackTrace st = new StackTrace(skipFrames + 1, true);
                    Message(_nanoFrameworkMessagesPane, $"{DateTime.Now.ToString("u")} [{message}: { st.ToString() }]");
                }
                else
                {
                    Message(_nanoFrameworkMessagesPane, $"{DateTime.Now.ToString("u")} [{ message }]");
                }
            }
        }

        public static void OutputMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            DebugMessage(outLine.Data);
        }

        /// <summary>
        /// Write a message to the nanoFramework output pane.
        /// </summary>
        /// <param name="message">Message to be outputted.</param>
        public static void OutputMessage(string message)
        {
            Message(_nanoFrameworkMessagesPane, message);
        }

        /// <summary>
        /// Write a message to the firmware update output pane.
        /// </summary>
        /// <param name="message">Message to be outputted.</param>
        public static void OutputFirmwareUpdateMessage(string message)
        {
            Message(
                _firmwareUpdatManager, 
                message,
                false);
        }

        public static void ErrorMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            DebugMessage(outLine.Data);
        }

        private static void Message(
            IVsOutputWindowPane pane, 
            string message,
            bool activatePane = true)
        {
            if (message == null)
            {
                message = "[no message string provided to MessageCentre.Message()" + new StackTrace().ToString();
            }

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // if the message already ends with a return & new line, strip it
                // this is the case with some messages from the target
                // in case the message has more than one and intends to output an empty line this will occur anyway as we are adding it bellow
                if(message.EndsWith("\r\n"))
                {
                    message = message.Substring(0, message.Length - 2);
                }

                if (activatePane)
                {
                    pane.Activate();
                }

                pane.OutputStringThreadSafe(message + "\r\n");
            });
        }

        public static void StartProgressMessage(string message)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // stock general animation icon
                object icon = (short)Constants.SBAI_General;


                // Make sure the status bar is not frozen  
                _statusBar.IsFrozen(out int frozen);

                if (frozen != 0)
                {
                    _statusBar.FreezeOutput(0);
                }

                _statusBar.SetText(message);

                // start icon animation
                _statusBar.Animation(1, ref icon);

            });
        }

        public static void StartMessageWithProgress(MessageWithProgress message)
        //    string message,
        //    uint nComplete, 
        //    uint nTotal)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Make sure the status bar is not frozen  
                _statusBar.IsFrozen(out int frozen);

                if (frozen != 0)
                {
                    _statusBar.FreezeOutput(0);
                }

                _statusBar.SetText("");

                // start progress bar
                _statusBar.Progress(ref progressCookie, 1, message.Message, message.Current, message.Total);

            });
        }

        public static void StopProgressMessage(string message = null)
        {

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // stock general animation icon
                object icon = (short)Constants.SBAI_General;

                // Make sure the status bar is not frozen  
                _statusBar.IsFrozen(out int frozen);

                if (frozen != 0)
                {
                    _statusBar.FreezeOutput(0);
                }

                if (string.IsNullOrEmpty(message))
                {
                    _statusBar.SetText(message);
                }
                else
                {
                    _statusBar.Clear();
                }

                // stop the animation
                _statusBar?.Animation(0, ref icon);

                // stop the progress bar
                if (progressCookie > 0)
                {
                    _statusBar?.Progress(ref progressCookie, 0, "", 0, 0);

                    // reset cookie
                    progressCookie = 0;
                }

            });
        }

    }
}

