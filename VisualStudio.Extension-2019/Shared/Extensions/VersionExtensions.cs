//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public static class VersionExtensions
    {
        /// <summary>
        /// Compares <see cref="Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version"/> and <see cref="System.Version"/> values.
        /// </summary>
        /// <param name="pane"></param>
        /// <param name="pszOutputString">Version value to compare to.</param>
        public static bool Equals(this Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version, Version value)
        {
            return (version.MajorVersion == value.Major &&
                    version.MinorVersion == value.Minor &&
                    version.BuildNumber == value.Build &&
                    version.RevisionNumber == value.Revision);
        }
    }
}
