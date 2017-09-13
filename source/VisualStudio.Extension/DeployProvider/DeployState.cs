//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class DeployState
    {
        public delegate void EndDeployDelegate();

        public Thread threadDeploy;
        public System.Windows.Forms.Control deployCallbackControl;
        public IVsOutputWindowPane outputWindowPane;
        public bool deploySuccess = true;
        public string deployOuputMessage;
        public string deployTaskItem;
    }
}
