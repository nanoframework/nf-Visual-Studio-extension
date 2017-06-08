//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal class DeployProvider : IDeployProvider
    {
        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter outputPaneWriter)
        {
            await outputPaneWriter.WriteAsync("This will deploy to the connected target");
        }

        public bool IsDeploySupported
        {
            get { return true; }
        }

        public void Commit()
        {
        }

        public void Rollback()
        {
        }
    }
}