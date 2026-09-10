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
    /// Compiling placeholder for the future Concord engine binding. It exists to
    /// prove the swap point and the MEF wiring: it is exported alongside the AD7
    /// binding and is selectable by configuration (EngineId = "Concord"), but its
    /// members throw until the engine is built.
    ///
    /// When implemented, model it on the Concord <b>Iris</b> sample:
    ///   - <see cref="EngineGuid"/> is the Concord engine GUID registered via a
    ///     <c>.vsdconfig</c> at install time (not a runtime constant).
    ///   - <see cref="PortSupplierGuid"/> is a Dkm transport / custom Dkm port.
    ///   - <see cref="CreateLaunchSettings"/> produces a Concord launch (engine
    ///     GUID, in-proc Dkm components, no shell-out exe).
    ///   - Execution control / breakpoints / stepping move to IDkm* components;
    ///     symbol mapping moves to a Concord symbol provider.
    /// Crucially, the <c>nf-debugger</c> wire-protocol client is reused unchanged.
    ///
    /// Selection is config-driven, so enabling Concord does not require touching
    /// any other layer — see <see cref="NanoDebuggerLaunchProvider"/>.
    /// </summary>
    [Export(typeof(INanoDebugEngineBinding))]
    internal sealed class ConcordEngineBinding : INanoDebugEngineBinding
    {
        internal const string Id = "Concord";

        public string EngineId => Id;

        public Guid EngineGuid =>
            throw new NotImplementedException(
                "Concord engine GUID is registered via .vsdconfig at install time. See the Concord Iris sample.");

        public Guid PortSupplierGuid =>
            throw new NotImplementedException(
                "Concord uses a Dkm transport / custom Dkm port. See the Concord Iris sample.");

        public DebugLaunchSettings CreateLaunchSettings(
            DebugLaunchOptions launchOptions,
            NanoDeviceBase device,
            IReadOnlyList<string> peFilesToLoad,
            IVsHierarchy project) =>
            throw new NotImplementedException(
                "ConcordEngineBinding is a POC stub. Implement on the Concord Iris model, reusing the shared nf-debugger wire-protocol client.");
    }
}
