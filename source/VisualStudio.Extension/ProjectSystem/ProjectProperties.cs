//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Export]
    [ExcludeFromCodeCoverage]
    internal partial class ProjectProperties : StronglyTypedPropertyAccess
    {
        public new ConfiguredProject ConfiguredProject
        {
            get { return base.ConfiguredProject; }
        }
    }
}
