//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// View > Other Windows > Device Explorer.
    /// </summary>
    [VisualStudioContribution]
    internal sealed class ShowDeviceExplorerCommand : Command
    {
        /// <inheritdoc />
        public override CommandConfiguration CommandConfiguration => new("Device Explorer")
        {
            Placements = new[] { CommandPlacement.KnownPlacements.ViewOtherWindowsMenu },
            Icon = new CommandIconConfiguration(ImageMoniker.Custom("NanoFramework"), IconSettings.IconAndText),
        };

        /// <inheritdoc />
        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            ExtensionTelemetry.TrackEvent("DeviceExplorerCommand", new TelemetryProperties { { "command", "show" } });

            await Extensibility.Shell().ShowToolWindowAsync<DeviceExplorerToolWindow>(activate: true, cancellationToken);
        }
    }
}
