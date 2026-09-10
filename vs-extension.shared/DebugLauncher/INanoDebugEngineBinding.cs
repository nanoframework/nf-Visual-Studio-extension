// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// The one seam that decouples <i>which Visual Studio debugger API</i> drives
    /// a session from <i>how nanoFramework launches and talks to the device</i>.
    ///
    /// Today there is a single implementation, <see cref="Ad7CorDebugEngineBinding"/>
    /// (the existing custom AD7 / <c>CorDebug</c> engine). A future Concord engine
    /// (<see cref="ConcordEngineBinding"/>) is a drop-in second implementation: the
    /// launch/deploy/project-system layers above this interface do not change, and
    /// the <c>nf-debugger</c> wire-protocol client below it is shared by both.
    ///
    /// Before this seam, <see cref="NanoDebuggerLaunchProvider"/> hard-coded
    /// <c>CorDebug.EngineGuid</c>, <c>DebugPortSupplier.PortSupplierGuid</c> and
    /// <c>CorDebugProcess</c>. Those three concerns now live behind here.
    /// </summary>
    internal interface INanoDebugEngineBinding
    {
        /// <summary>
        /// Stable id used to select a binding by configuration (e.g. "AD7",
        /// "Concord"). The launcher resolves the active binding by this id.
        /// </summary>
        string EngineId { get; }

        /// <summary>Identity VS uses to select the debug engine.</summary>
        Guid EngineGuid { get; }

        /// <summary>Identity of the transport / port supplier the engine uses.</summary>
        Guid PortSupplierGuid { get; }

        /// <summary>
        /// Build the engine-specific launch settings from nano-level inputs. The
        /// <paramref name="peFilesToLoad"/> list is produced by the shared,
        /// engine-agnostic <see cref="ReferenceCrawler"/> in the launcher.
        /// </summary>
        DebugLaunchSettings CreateLaunchSettings(
            DebugLaunchOptions launchOptions,
            NanoDeviceBase device,
            IReadOnlyList<string> peFilesToLoad,
            IVsHierarchy project);
    }
}
