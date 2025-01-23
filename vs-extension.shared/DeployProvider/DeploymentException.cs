//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Serializable]
    internal class DeploymentException : Exception
    {
        public DeploymentException()
        {
        }

        public DeploymentException(string message) : base(message)
        {
        }

        public DeploymentException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DeploymentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}