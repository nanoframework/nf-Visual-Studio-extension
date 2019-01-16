//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class DeploymentAssembly
    {
        public string Path { get; set; }
        public string Version { get; set; }

        public DeploymentAssembly(string path, string version)
        {
            Path = path;
            Version = version;
        }
    }
}
