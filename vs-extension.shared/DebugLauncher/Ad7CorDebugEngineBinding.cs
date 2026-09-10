// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// The current engine binding: the custom AD7 engine (<see cref="CorDebug"/>)
    /// hosted out of process by <see cref="CorDebugProcess"/>, reached through the
    /// custom <see cref="DebugPortSupplier"/>. This is a faithful extraction of the
    /// settings <see cref="NanoDebuggerLaunchProvider"/> used to build inline — no
    /// behavior change, just relocation behind <see cref="INanoDebugEngineBinding"/>.
    /// </summary>
    [Export(typeof(INanoDebugEngineBinding))]
    internal sealed class Ad7CorDebugEngineBinding : INanoDebugEngineBinding
    {
        internal const string Id = "AD7";

        public string EngineId => Id;

        public Guid EngineGuid => CorDebug.EngineGuid;

        public Guid PortSupplierGuid => DebugPortSupplier.PortSupplierGuid;

        public DebugLaunchSettings CreateLaunchSettings(
            DebugLaunchOptions launchOptions,
            NanoDeviceBase device,
            IReadOnlyList<string> peFilesToLoad,
            IVsHierarchy project)
        {
            // The AD7 engine is driven by the CorDebugProcess command line:
            // /waitfordebugger, one /load:<pe> per assembly, then the device name
            // using the CorDebugProcess.DeployDeviceName contract.
            var cb = new CommandLineBuilder();
            cb.AddArguments("/waitfordebugger");

            foreach (string peFile in peFilesToLoad)
            {
                cb.AddArguments("/load:" + peFile);
            }

            string commandLine = Environment.ExpandEnvironmentVariables(cb.ToString());
            commandLine = string.Format(
                "{0} \"{1}{2}\"",
                commandLine,
                CorDebugProcess.DeployDeviceName,
                device.Description);

            return new DebugLaunchSettings(launchOptions)
            {
                Executable = typeof(CorDebugProcess).Assembly.Location,
                Arguments = commandLine,
                LaunchOperation = DebugLaunchOperation.CreateProcess,
                PortSupplierGuid = PortSupplierGuid,
                // Use the device chosen for THIS launch (same instance used for the
                // command line above), not the global NanoDeviceCommService.Device.
                // They're equal for a single device, but the per-device Run-dropdown
                // selector needs PortName to follow the chosen device. See
                // poc-sdk-style/DEVICE-RUN-DROPDOWN.md.
                PortName = device.Description,
                Project = project,
                LaunchDebugEngineGuid = EngineGuid
            };
        }
    }
}
