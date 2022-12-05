////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal interface IVirtualDeviceService
    {
        bool NanoClrInstalled { get; }

        bool VirtualDeviceIsRunning { get; }

        Task InitVirtualDeviceAsync();

        void InstallNanoClrTool();

        void UpdateNanoClr();

        string ListVirtualSerialPorts();

        bool CreateVirtualSerialPort(string portName, out string executionLog);

        void StopVirtualDevice();

        Task<bool> StartVirtualDeviceAsync(bool rescanDevices);
    }
}
