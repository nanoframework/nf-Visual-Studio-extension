//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    //[Export(typeof(IProjectGlobalPropertiesProvider))]
    //[AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    //internal class GlobalPropertiesProvider : StaticGlobalPropertiesProviderBase
    //{
    //    public const string NanoCSharpProjectPathPropertyName = "NanoCSharpProjectPath";

    //    [ImportingConstructor]
    //    internal GlobalPropertiesProvider(IProjectService projectService)
    //        : base(projectService.Services)
    //    { }

    //    //[ImportingConstructor]
    //    //internal GlobalPropertiesProvider(IThreadHandling threadHandling)
    //    //    : base(threadHandling.JoinableTaskContext)
    //    //{
    //    //}

    //    public override Task<IImmutableDictionary<string, string>> GetGlobalPropertiesAsync(CancellationToken cancellationToken) =>
    //        Task.FromResult<IImmutableDictionary<string, string>>(
    //            Empty.PropertiesMap.SetItem(
    //                NanoCSharpProjectPathPropertyName,
    //                Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "Targets")));

    //}
}
