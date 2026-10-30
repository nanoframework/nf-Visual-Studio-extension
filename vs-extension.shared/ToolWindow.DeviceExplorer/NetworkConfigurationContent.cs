//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.Extensibility.UI;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Content of the network configuration dialog, defined in NetworkConfigurationContent.xaml.
    /// </summary>
    internal sealed class NetworkConfigurationContent : RemoteUserControl
    {
        public NetworkConfigurationContent(NetworkConfigurationViewModel viewModel)
            : base(dataContext: viewModel)
        {
        }
    }
}
