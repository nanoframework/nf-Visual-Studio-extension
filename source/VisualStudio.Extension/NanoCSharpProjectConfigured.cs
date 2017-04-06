//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using Microsoft.VisualStudio.ProjectSystem;
    using System.ComponentModel.Composition;
    using System.Diagnostics.CodeAnalysis;

    [Export]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal class NanoCSharpProjectConfigured
    {
        [Import, SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "MEF")]
        internal ConfiguredProject ConfiguredProject { get; private set; }

        [Import, SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "MEF")]
        internal ProjectProperties Properties { get; private set; }
    }
}
