//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using nanoFramework.Tools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace nanoFramework.Tools
{
    /// <summary>
    /// Generates the strongly typed code-behind (Designer.cs) for .resx files that use the
    /// .NET nanoFramework custom tool (nFResXFileCodeGenerator), so the code is generated at
    /// build time even if VS never ran the custom tool (e.g. the file was deleted or the
    /// project is built from the command line).
    /// The output is the same as the custom tool's because both use <see cref="ProcessResourceFiles"/>.
    /// </summary>
    public class GenerateNanoResourceDesignerTask : Task
    {
        /// <summary>
        /// The .resx files to process. Each item must have the following metadata:
        /// StronglyTypedFileName (path of the code file to write), StronglyTypedClassName and StronglyTypedNamespace.
        /// </summary>
        [Required]
        public ITaskItem[] Sources { get; set; }

        /// <summary>
        /// The code files that were generated or were already up to date.
        /// </summary>
        [Output]
        public ITaskItem[] GeneratedFiles { get; private set; }

        public override bool Execute()
        {
            // wait for debugger on var
            DebuggerHelper.WaitForDebuggerIfEnabled(TasksConstants.BuildTaskDebugVar);

            List<ITaskItem> generatedFiles = new List<ITaskItem>();

            if (Sources == null || Sources.Length == 0)
            {
                GeneratedFiles = generatedFiles.ToArray();
                return true;
            }

            // use a separate AppDomain so the .resx and the files it links to aren't kept locked
            AppDomain appDomain = null;

            try
            {
                appDomain = AppDomain.CreateDomain(
                    "generateResourceDesignerAppDomain",
                    null,
                    AppDomain.CurrentDomain.SetupInformation);

                foreach (ITaskItem source in Sources)
                {
                    string outputFile = source.GetMetadata("StronglyTypedFileName");
                    string className = source.GetMetadata("StronglyTypedClassName");
                    string classNamespace = source.GetMetadata("StronglyTypedNamespace");

                    if (string.IsNullOrEmpty(outputFile) || string.IsNullOrEmpty(className))
                    {
                        Log.LogError($"Can't generate code-behind for '{source.ItemSpec}': StronglyTypedFileName and StronglyTypedClassName metadata are required.");
                        continue;
                    }

                    try
                    {
                        ProcessResourceFiles process = (ProcessResourceFiles)appDomain.CreateInstanceFromAndUnwrap(
                            typeof(ProcessResourceFiles).Module.FullyQualifiedName,
                            typeof(ProcessResourceFiles).FullName);

                        string code = process.CreateStronglyTypedResourcesCode(
                            source.ItemSpec,
                            className,
                            classNamespace);

                        // only touch the file if the content changed, to avoid needless recompiles and source control churn
                        if (!File.Exists(outputFile) || File.ReadAllText(outputFile) != code)
                        {
                            // same encoding as the VS custom tool output (UTF-8 without BOM)
                            File.WriteAllText(outputFile, code, new UTF8Encoding(false));

                            Log.LogMessage(MessageImportance.Normal, $"Generated code-behind '{outputFile}' from '{source.ItemSpec}'.");
                        }
                        else
                        {
                            // bump the timestamp so the target is skipped on the next incremental build
                            File.SetLastWriteTimeUtc(outputFile, DateTime.UtcNow);
                        }

                        generatedFiles.Add(new TaskItem(outputFile));
                    }
                    catch (Exception ex)
                    {
                        Exception reported = ex is TargetInvocationException tie && tie.InnerException != null
                            ? tie.InnerException
                            : ex;

                        Log.LogError($"Error generating code-behind for '{source.ItemSpec}': {reported.Message}");
                    }
                }
            }
            finally
            {
                if (appDomain != null)
                {
                    AppDomain.Unload(appDomain);
                }
            }

            GeneratedFiles = generatedFiles.ToArray();

            return !Log.HasLoggedErrors;
        }
    }
}
