//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public abstract class MessageCentreBase
    {
        protected static readonly Guid s_InternalErrorsPaneGuid     = Guid.NewGuid();
        protected static readonly Guid s_DeploymentMessagesPaneGuid = Guid.NewGuid();

        //--//

        public abstract void DebugMessage(string message);

        public abstract void ClearDeploymentMessages();

        public abstract void DeploymentMessage(string message);

        public abstract void InternalErrorMessage(string message);
        
        public abstract void InternalErrorMessage(bool assertion, string message);

        public abstract void InternalErrorMessage(bool assertion, string message, int skipFrames);

        public abstract void OutputMessageHandler(object sendingProcess, DataReceivedEventArgs outLine);

        public abstract void ErrorMessageHandler(object sendingProcess, DataReceivedEventArgs outLine);

        public abstract void StartProgressMessage(string message);

        public abstract void StopProgressMessage(string message);

        public void StopProgressMessage()
        {
            StopProgressMessage(null);
        }
    }

    public class NullMessageCentre : MessageCentreBase
    {
        public override void DebugMessage(string message)
        {
        }

        public override void ClearDeploymentMessages()
        {
        }

        public override void DeploymentMessage(string message)
        {
        }

        public override void InternalErrorMessage(string message)
        {
        }

        public override void InternalErrorMessage(bool assertion, string message)
        {
        }

        public override void InternalErrorMessage(bool assertion, string message, int skipFrames)
        {
        }

        public override void OutputMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
        }

        public override void ErrorMessageHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
        }

        public override void StartProgressMessage(string message)
        {
        }

        public override void StopProgressMessage(string message)
        {
        }
    }
}

