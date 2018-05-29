//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class MessageCentre
    {
        protected static readonly Guid s_InternalErrorsPaneGuid = Guid.NewGuid();
        protected static readonly Guid s_DeploymentMessagesPaneGuid = Guid.NewGuid();

        private static IVsOutputWindow _outputWindow;
        private static IVsOutputWindowPane _debugPane;
        private static IVsOutputWindowPane _nanoFrameworkMessagesPane;
        private static IVsStatusbar _statusBar;
        private static string _paneName;

        public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package, string name)
        {
            // seems OK to call these API here without switching to the main thread as we are just getting the service not actually accessing the output window
#pragma warning disable VSTHRD010
            _outputWindow = await package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            _statusBar = await package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
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
                _outputWindow.CreatePane(ref tempId, _paneName, 0, 1);
                _outputWindow.GetPane(ref tempId, out _nanoFrameworkMessagesPane);
            });
        }

        public static void DebugMessage(string message)
        {
            Message(_debugPane, message ?? "");
        }

        public static void ClearDeploymentMessages()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
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

        public static void OutputMessage(string message)
        {
            Message(_nanoFrameworkMessagesPane, message);
        }

        public static void ErrorMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            DebugMessage(outLine.Data);
        }

        private static void Message(IVsOutputWindowPane pane, String message)
        {
            if (message == null)
            {
                message = "[no message string provided to MessageCentre.Message()" + new StackTrace().ToString();
            }

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                pane.Activate();
                pane.OutputStringThreadSafe(message + "\r\n");
            });
        }

        public static void StartProgressMessage(string message)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // stock general animation icon
                object icon = (short)Constants.SBAI_General;


                // Make sure the status bar is not frozen  
                int frozen;
                _statusBar.IsFrozen(out frozen);

                if (frozen != 0)
                {
                    _statusBar.FreezeOutput(0);
                }

                _statusBar.SetText(message);

                // start icon animation
                _statusBar.Animation(1, ref icon);

            });
        }

        public static void StopProgressMessage(string message = null)
        {

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // stock general animation icon
                object icon = (short)Constants.SBAI_General;

                // Make sure the status bar is not frozen  
                int frozen;
                _statusBar.IsFrozen(out frozen);

                if (frozen != 0)
                {
                    _statusBar.FreezeOutput(0);
                }


                if (String.IsNullOrEmpty(message))
                {
                    _statusBar.SetText(message);
                }
                else
                {
                    _statusBar.Clear();
                }

                // stop the animation
                _statusBar?.Animation(0, ref icon);

            });
        }

    }
}

