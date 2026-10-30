//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Content of the Device Explorer tool window, defined in DeviceExplorerContent.xaml.
    /// </summary>
    internal sealed class DeviceExplorerContent : RemoteUserControl
    {
        private readonly DeviceExplorerRemoteViewModel _viewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceExplorerContent"/> class.
        /// </summary>
        /// <param name="getExtensibility">Gets the extensibility object of the tool window, to show dialogs.</param>
        public DeviceExplorerContent(Func<VisualStudioExtensibility> getExtensibility)
            : this(new DeviceExplorerRemoteViewModel(getExtensibility))
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
