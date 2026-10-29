//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Content of the Device Explorer tool window, defined in DeviceExplorerContent.xaml.
    /// </summary>
    internal sealed class DeviceExplorerContent : RemoteUserControl
    {
        private readonly DeviceExplorerRemoteViewModel _viewModel;

        public DeviceExplorerContent()
            : this(new DeviceExplorerRemoteViewModel())
        {
        }

        private DeviceExplorerContent(DeviceExplorerRemoteViewModel viewModel)
            : base(dataContext: viewModel)
        {
            _viewModel = viewModel;
        }

        /// <inheritdoc />
        public override async Task ControlLoadedAsync(CancellationToken cancellationToken)
        {
            await base.ControlLoadedAsync(cancellationToken);

            await _viewModel.InitializeAsync(cancellationToken);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _viewModel.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
