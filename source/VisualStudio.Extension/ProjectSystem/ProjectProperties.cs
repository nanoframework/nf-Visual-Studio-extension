//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Export]
    internal partial class ProjectProperties : StronglyTypedPropertyAccess
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectProperties"/> class.
        /// </summary>
        [ImportingConstructor]
        public ProjectProperties(ConfiguredProject configuredProject)
            : base(configuredProject)
        {
            // not sure if this is required when using the debugger from the command line (untested...)
            //try
            //{
            //    ActivateDebugEngine();
            //}
            //catch (Exception e)
            //{
            //    MessageCentre.InternalErrorMessage(false, String.Format("Unable to register debug engine: {0}", e.Message));
            //}
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectProperties"/> class.
        /// </summary>
        public ProjectProperties(ConfiguredProject configuredProject, string file, string itemType, string itemName)
            : base(configuredProject, file, itemType, itemName)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectProperties"/> class.
        /// </summary>
        public ProjectProperties(ConfiguredProject configuredProject, IProjectPropertiesContext projectPropertiesContext)
            : base(configuredProject, projectPropertiesContext)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectProperties"/> class.
        /// </summary>
        public ProjectProperties(ConfiguredProject configuredProject, UnconfiguredProject unconfiguredProject)
            : base(configuredProject, unconfiguredProject)
        {
        }

        public new ConfiguredProject ConfiguredProject
        {
            get { return base.ConfiguredProject; }
        }

        static uint debugEngineCmdUICookie = 0;

        private void ActivateDebugEngine()
        {
            // The debug engine will not work unless we enable a CmdUIContext using the engine's GUID.
            if (debugEngineCmdUICookie == 0)
            {
                IVsMonitorSelection monitorSelection = ServiceProvider.GlobalProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
                if (monitorSelection == null)
                {
                    throw new InvalidOperationException(String.Format("Missing service {0}!", typeof(IVsMonitorSelection).FullName));
                }
                Guid guidDebugEngine = CorDebug.EngineGuid;//DebuggerGuids.EngineId; //

                int hr = monitorSelection.GetCmdUIContextCookie(ref guidDebugEngine, out debugEngineCmdUICookie);
                if (ErrorHandler.Succeeded(hr))
                {
                    ErrorHandler.ThrowOnFailure(monitorSelection.SetCmdUIContext(debugEngineCmdUICookie, 1));
                }
                else
                {
                    // GetCmdUIContextCookie is expected to fail if the IDE has been launched
                    // in command line mode. Verify that it's unexpected before throwing.
                    IVsShell vsShell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
                    if (vsShell != null)
                    {
                        object inCmdLineMode;
                        ErrorHandler.ThrowOnFailure(vsShell.GetProperty((int)__VSSPROPID.VSSPROPID_IsInCommandLineMode, out inCmdLineMode));
                        if (inCmdLineMode is bool)
                        {
                            if ((bool)inCmdLineMode)
                            {
                                hr = VSConstants.S_OK;
                            }
                        }
                    }
                    // Reset hr to S_OK to avoid throwing here if the failure was expected.
                    ErrorHandler.ThrowOnFailure(hr);
                }
            }
        }

    }
}
