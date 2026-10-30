//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// The Device Explorer tool window.
    /// </summary>
    [VisualStudioContribution]
    internal sealed class DeviceExplorerToolWindow : Microsoft.VisualStudio.Extensibility.ToolWindows.ToolWindow
    {
        // Solution Explorer: Device Explorer shows tabbed with it, by default
        private static readonly Guid SolutionExplorerGuid = new Guid("3ae79031-e1bc-11d0-8f78-00a0c9110057");

        private readonly DeviceExplorerContent _content;

        public DeviceExplorerToolWindow()
        {
            Title = "Device Explorer";

            // the dialogs of the tool window are shown through the extensibility object, it's set after construction
            _content = new DeviceExplorerContent(() => Extensibility);
        }

        /// <inheritdoc />
        public override ToolWindowConfiguration ToolWindowConfiguration => new()
        {
            Placement = ToolWindowPlacement.DockedTo(SolutionExplorerGuid),
            AllowAutoCreation = true,
        };

        /// <inheritdoc />
        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
            => Task.FromResult<IRemoteUserControl>(_content);

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _content.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
