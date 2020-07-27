//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.VisualStudio.Extension
{
    using Microsoft;
    using Microsoft.VisualStudio.ProjectSystem;
    using Microsoft.VisualStudio.Shell.Interop;
    using System.ComponentModel.Composition;

    [Export]
    [AppliesTo(UniqueCapability)]
    internal class NanoCSharpProjectUnconfigured
    {
        /// <summary>
        /// The file extension used by your project type.
        /// This does not include the leading period.
        /// </summary>
        internal const string ProjectExtension = "nfproj";

        /// <summary>
        /// A project capability that is present in your project type and none others.
        /// This is a convenient constant that may be used by your extensions so they
        /// only apply to instances of your project type.
        /// </summary>
        /// <remarks>
        /// This value should be kept in sync with the capability as actually defined in your .targets.
        /// </remarks>
        internal const string UniqueCapability = "NanoCSharpProject";

        internal const string Language = "csharp";

        [ImportingConstructor]
        public NanoCSharpProjectUnconfigured(UnconfiguredProject unconfiguredProject)
        {
            Requires.NotNull(unconfiguredProject, nameof(unconfiguredProject));
            ProjectHierarchies = new OrderPrecedenceImportCollection<IVsHierarchy>(projectCapabilityCheckProvider: unconfiguredProject);
        }

        [Import]
        internal UnconfiguredProject UnconfiguredProject { get; private set; }

        [Import]
        internal IActiveConfiguredProjectSubscriptionService SubscriptionService { get; private set; }

        [Import]
        internal IProjectThreadingService ProjectThreadingService { get; private set; }

        [Import]
        internal ActiveConfiguredProject<ConfiguredProject> ActiveConfiguredProject { get; private set; }

        [Import]
        internal ActiveConfiguredProject<NanoCSharpProjectConfigured> MyActiveConfiguredProject { get; private set; }

        [ImportMany(ExportContractNames.VsTypes.IVsProject, typeof(IVsProject))]
        internal OrderPrecedenceImportCollection<IVsHierarchy> ProjectHierarchies { get; private set; }

        internal IVsHierarchy ProjectHierarchy => ProjectHierarchies.Single().Value;

        [Import(ExportContractNames.Scopes.UnconfiguredProject)]
        IProjectAsynchronousTasksService AsyncTasksService { get; set; }
    }
}
