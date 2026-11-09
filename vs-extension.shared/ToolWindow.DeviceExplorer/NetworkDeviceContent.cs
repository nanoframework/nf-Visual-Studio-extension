//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Extensibility.UI;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Content of the dialog to enter the token of a network device, defined in NetworkDeviceContent.xaml.
    /// </summary>
    internal sealed class NetworkDeviceContent : RemoteUserControl
    {
        public NetworkDeviceContent(NetworkDeviceViewModel viewModel)
            : base(dataContext: viewModel)
        {
        }
    }
}
