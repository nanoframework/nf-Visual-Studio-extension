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
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class MessageCentre : MessageCentreBase
    {
        private IVsOutputWindow     _outputWindow;
        private IVsOutputWindowPane _debugPane;
        private IVsOutputWindowPane _deploymentMessagesPane;
        private IVsStatusbar        _statusBar;
        private bool                _showInternalErrors;

        public MessageCentre()
        {
            _outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;

            if(_outputWindow == null) throw new Exception( "Package.GetGlobalService(SVsOutputWindow) failed to provide the output window" );

            Guid tempId = VSConstants.GUID_OutWindowDebugPane;
            _outputWindow.GetPane(ref tempId, out _debugPane);

            tempId = s_DeploymentMessagesPaneGuid;
            _outputWindow.CreatePane(ref tempId, "nanoFramework Extension", 0, 1);

            tempId = s_DeploymentMessagesPaneGuid;
            _outputWindow.GetPane(ref tempId, out _deploymentMessagesPane);

            _showInternalErrors = false;
            // TODO replace with project user option exposed in device explorer
            //if (RegistryAccess.GetBoolValue(@"\NonVersionSpecific\UserInterface", "showInternalErrors", out m_fShowInternalErrors, false))
            //{
            //    this.Message(m_deploymentMessagesPane, "nanoFramework deployment internal errors will be reported.");
            //}

            _statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
        }

        public override void DebugMessage(string Message)
        {
            this.Message(_debugPane, Message==null?"":Message);
        }

        public override void ClearDeploymentMessages()
        {
            try
            {
                if (_deploymentMessagesPane != null)
                    _deploymentMessagesPane.Clear();
            }
            catch (InvalidOperationException)
            {
            }
        }

        public override void DeploymentMessage(string message)
        {
            this.Message(_deploymentMessagesPane, message);
        }

        public override void InternalErrorMessage(string message)
        {
            this.InternalErrorMessage(false, message);
        }

        public override void InternalErrorMessage(bool assertion, string message)
        {
            this.InternalErrorMessage(assertion, message, -1);
        }

        public override void InternalErrorMessage(bool assertion, string message, int skipFrames)
        {
            if (!assertion && _showInternalErrors)
            {
                message = String.IsNullOrEmpty(message) ? "Unknown Error" : message;
                
                if (skipFrames >= 0)
                {
                    StackTrace st = new StackTrace(skipFrames + 1, true);
                    this.Message(_deploymentMessagesPane, String.Format("[@ {0}: {1} @]", message, st.ToString()));
                }
                else
                {
                    this.Message(_deploymentMessagesPane, "[@ " + message + " @]");
                }
            }
        }

        public override void OutputMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            this.DebugMessage(outLine.Data);
        }


        public override void ErrorMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            this.DebugMessage(outLine.Data);
        }

        private void Message(IVsOutputWindowPane pane, String message)
        {
            if(pane == null)
                return;

            if(message==null)
                message = "[no message string provided to MessageCentre.Message()" + new StackTrace().ToString();

            try
            {
                lock (pane)
                {
                    pane.Activate();
                    pane.OutputStringThreadSafe(message + "\r\n");
                }
            }
            catch( InvalidComObjectException )
            {
            }

        }

        public override void StartProgressMessage(string message)
        {
            // stock general animation icon
            object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;

            try
            {

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
            }
            catch (InvalidOperationException)
            {
            }

        }

        public override void StopProgressMessage(string message)
        {
            // stock general animation icon
            object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;

            try
            {
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
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}

