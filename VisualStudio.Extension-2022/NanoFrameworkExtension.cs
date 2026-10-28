// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.Extensibility;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Entry point for the VisualStudio.Extensibility side of the extension.
    /// The package, debug engine, project system and MEF parts are still VSSDK, so the extension is hosted in-process.
    /// </summary>
    [VisualStudioContribution]
    internal class NanoFrameworkExtension : Microsoft.VisualStudio.Extensibility.Extension
    {
        /// <inheritdoc />
        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            RequiresInProcessHosting = true,
        };
    }
}
