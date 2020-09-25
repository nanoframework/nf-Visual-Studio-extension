//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.ComponentModel;
using System.Linq;

namespace nanoFramework.Tools
{
    [Description("ResolveRuntimeDependenciesTaskEntry")]
    public class ResolveRuntimeDependenciesTask : Task
    {
        #region public properties for the task

        public string Assembly { get; set; }

        public string StartProgram { get; set; }

        public ITaskItem[] AssemblyReferences { get; set; }

        public ITaskItem[] StartProgramReferences { get; set; }

        #endregion


        public override bool Execute()
        {
            // report to VS output window what step the build is 
            Log.LogMessage(MessageImportance.Normal, "Resolving Runtime Dependencies for nanoFramework assembly...");

            try
            {
                object host = HostObject;

                if (host != null)
                {
                    Type typ = host.GetType();

                    typ.GetProperty("Assembly").SetValue(host, Assembly, null);
                    typ.GetProperty("StartProgram").SetValue(host, StartProgram, null);
                    typ.GetProperty("AssemblyReferences").SetValue(host, GetFullPathFromItems(AssemblyReferences), null);
                    typ.GetProperty("StartProgramReferences").SetValue(host, GetFullPathFromItems(StartProgramReferences), null);
                }
            }
            catch (Exception ex)
            {
                Log.LogError("nanoFramework ResolveRuntimeDependenciesTask error: " + ex.Message);
            }

            // if we've logged any errors that's because there were errors (WOW!)
            return !Log.HasLoggedErrors;
        }

        protected string[] GetFullPathFromItems(ITaskItem[] items)
        {
            return items?.Select((item) => {
                return item.GetMetadata("FullPath");
            }).ToArray();
        }
    }
}
