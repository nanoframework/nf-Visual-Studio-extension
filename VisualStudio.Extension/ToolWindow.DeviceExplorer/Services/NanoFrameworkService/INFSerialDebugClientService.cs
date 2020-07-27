//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension.ToolWindow
{
    public interface INFSerialDebugClientService : INFDebugClientBaseService
    {
        PortBase SerialDebugClient { get; }
    }
}
