//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class DeploymentAssembly
    {
        /// <summary>
        /// Path to the PE file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Assembly version, as stored in the PE header.
        /// </summary>
        public string Version { get; set; }

        public DeploymentAssembly(string path, string version)
        {
            Path = path;
            Version = version;
        }
    }
}
