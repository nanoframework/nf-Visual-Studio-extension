//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using nanoFramework.Tools.Utilities;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools
{
    [Description("GenerateBinaryOutputTaskEntry")]

    public class GenerateBinaryOutputTask : Task
    {
        #region public properties for the task

        public string Assembly { get; set; }

        public string AssemblyPE { get; set; }

        public ITaskItem[] AssemblyReferences { get; set; }


        /// <summary>
        /// The name(s) of binary file created.
        /// </summary>
        [Output]
        public ITaskItem FileWritten { get; private set; }

        #endregion

        public override bool Execute()
        {
            // report to VS output window what step the build is 
            Log.LogMessage(MessageImportance.Normal, "Generating binary output file...");

            // wait for debugger on var
            DebuggerHelper.WaitForDebuggerIfEnabled(TasksConstants.BuildTaskDebugVar);

            // default with null, indicating that we've generated nothing
            FileWritten = null;

            // get paths for PE files
            // rename extension .dll with .pe
            List<string> peCollection = new List<string>();
            peCollection = AssemblyReferences?.Select(a => { return a.GetMetadata("FullPath").Replace(".dll", ".pe").Replace(".exe", ".pe"); }).ToList();

            // add executable PE
            peCollection.Add(AssemblyPE);

            // get executable path and file name
            // rename executable extension .exe with .bin
            var binOutputFile = Assembly.Replace(".exe", ".bin");

            using (FileStream binFile = new FileStream(binOutputFile, FileMode.Create))
            {
                // now we will re-deploy all system assemblies
                foreach (string peItem in peCollection)
                {
                    // append to the deploy blob the assembly
                    using (FileStream fs = File.Open(peItem, FileMode.Open, FileAccess.Read))
                    {
                        long length = (fs.Length + 3) / 4 * 4;
                        byte[] buffer = new byte[length];

                        fs.Read(buffer, 0, (int)fs.Length);

                        // copy this assembly to the bin file too
                        binFile.Write(buffer, 0, (int)length);
                    }
                }
            }

            // bin file written
            FileWritten = new TaskItem(binOutputFile);

            return true;
        }
    }
}
