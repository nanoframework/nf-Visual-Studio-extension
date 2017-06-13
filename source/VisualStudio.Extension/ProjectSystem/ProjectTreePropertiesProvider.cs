//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using System.ComponentModel.Composition;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Updates nodes in the project tree by overriding property values calcuated so far by lower priority providers.
    /// </summary>
    [Export(typeof(IProjectTreePropertiesProvider))]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal class ProjectTreePropertiesProvider : IProjectTreePropertiesProvider
    {
        /// <summary>
        /// Calculates new property values for each node in the project tree.
        /// </summary>
        /// <param name="propertyContext">Context information that can be used for the calculation.</param>
        /// <param name="propertyValues">Values calculated so far for the current node by lower priority tree properties providers.</param>
        public void CalculatePropertyValues(
            IProjectTreeCustomizablePropertyContext propertyContext,
            IProjectTreeCustomizablePropertyValues propertyValues)
        {
            // set the icon for the root project node
            if (propertyValues.Flags.Contains(ProjectTreeFlags.Common.ProjectRoot))
            {
                propertyValues.Icon = NanoFrameworkMonikers.NanoFrameworkProject.ToProjectSystemType();
                propertyValues.ExpandedIcon = NanoFrameworkMonikers.NanoFrameworkProject.ToProjectSystemType();
            }
        }
    }
}