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
        /// Path to the EXE or DLL file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Assembly version of the EXE or DLL.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Required version of the native implementation of the class library.
        /// Only used in class libraries. Can be empty on the core library and user EXE and DLLs.
        /// </summary>
        public string NativeVersion { get; set; }

        public DeploymentAssembly(string path, string version, string nativeVersion)
        {
            Path = path;
            Version = version;
            NativeVersion = nativeVersion;
        }
    }
}
