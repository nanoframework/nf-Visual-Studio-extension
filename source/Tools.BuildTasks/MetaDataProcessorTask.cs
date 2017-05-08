//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.ComponentModel;
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace nanoFramework.Tools
{
    [Description("MetaDataProcessorTaskEntry")]
    public class MetaDataProcessorTask : ToolTask  // TODO this will be replace with a simple Task when we have MetaDataProcessor in C#
    {
        private string tempDataBaseFile = null;


        #region public properties for the task

        /// <summary>
        /// Array of nanoFramework assemblies to be passed to MetaDataProcessor in -loadHints switch 
        /// </summary>
        public ITaskItem[] LoadHints { get; set; }

        public ITaskItem[] IgnoreAssembly { get; set; }

        public ITaskItem[] Load { get; set; }

        public ITaskItem[] LoadDatabase { get; set; }

        public string LoadStrings { get; set; }

        public ITaskItem[] ExcludeClassByName { get; set; }

        public ITaskItem[] ImportResources { get; set; }

        public string Parse { get; set; }

        public bool Minimize { get; set; }

        public string GenerateStringsTable { get; set; }

        public string Compile { get; set; }

        public bool Verbose { get; set; }

        public bool VerboseMinimize { get; set; }

        public bool NoByteCode { get; set; }

        public bool NoAttributes { get; set; }

        public ITaskItem[] CreateDatabase { get; set; }

        public string GenerateSkeletonFile { get; set; }

        public string GenerateSkeletonName { get; set; }

        public string GenerateSkeletonProject { get; set; }

        public string GenerateDependency { get; set; }

        public string CreateDatabaseFile { get; set; }

        public bool Resolve { get; set; }

        public string RefreshAssemblyName { get; set; }

        public string RefreshAssemblyOutput { get; set; }

        public string SaveStrings { get; set; }

        public string DumpAll { get; set; }

        public string DumpExports { get; set; }

        /// <summary>
        /// Sets wether the command line output is sent to the Log to help debugging command execution.
        /// Default is false.
        /// </summary>
        public bool OutputCommandLine { private get; set; } = false;

        private List<ITaskItem> _FilesWritten = new List<ITaskItem>();

        [Output]
        public ITaskItem[] FilesWritten { get { return _FilesWritten.ToArray(); } private set {  } }


        #endregion


        public override bool Execute()
        {
            // report to VS output window what step the build is 
            Log.LogCommandLine(MessageImportance.Normal, "Starting nanoFramework MetaDataProcessor...");

            try
            {
                // execute the ToolTask base method which is running the command line to call MetaDataProcessor will the appropriate parameters
                base.Execute();

                RecordFilesWritten();
            }
            catch (Exception ex)
            {
                Log.LogError("nanoFramework build error: " + ex.Message);
            }
            finally
            {
                Cleanup();
            }

            // if we've logged any errors that's because there were errors (WOW!)
            return !Log.HasLoggedErrors;
        }

        private void RecordFileWritten(string file)
        {
            if (!string.IsNullOrEmpty(file))
            {
                if (File.Exists(file))
                {
                    _FilesWritten.Add(new TaskItem(file));
                }
            }
        }

        private void RecordFilesWritten()
        {
            RecordFileWritten(SaveStrings);
            RecordFileWritten(GenerateStringsTable);
            RecordFileWritten(DumpAll);
            RecordFileWritten(DumpExports);
            RecordFileWritten(Compile);
            RecordFileWritten(Path.ChangeExtension(Compile, "pdbx"));
            RecordFileWritten(RefreshAssemblyOutput);
            RecordFileWritten(CreateDatabaseFile);
            RecordFileWritten(GenerateDependency);
        }

        private void Cleanup()
        {
            if (tempDataBaseFile != null)
                File.Delete(tempDataBaseFile);
        }

        protected override string ToolName
        {
            get
            {
                return "nanoFramework.Tools.MetaDataProcessor.exe";
            }
        }

        protected override string GenerateFullPathToTool()
        {
            return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "nanoFramework.Tools.MetaDataProcessor.exe");
        }

        protected override string GenerateCommandLineCommands()
        {
            // build command line to execute MetaDataProcessor
            CommandLineBuilder commandLinedBuilder = new CommandLineBuilder();

            // going through all possible options for MetaDataProcessor now...

            // -loadHints
            AppendLoadHints(commandLinedBuilder);

            // -load
            commandLinedBuilder.AppendSwitchForEachFile("-load", Load);

            // -loadDatabase
            commandLinedBuilder.AppendSwitchForEachFile("-loadDatabase", LoadDatabase);

            // -loadStrings
            commandLinedBuilder.AppendSwitchForFile("-loadStrings", LoadStrings);

            // -excludeClassByName
            commandLinedBuilder.AppendSwitchForEachFile("-excludeClassByName", ExcludeClassByName);

            // -parse
            commandLinedBuilder.AppendSwitchForFile("-parse", Parse);

            // -minimize
            commandLinedBuilder.AppendSwitchIfTrue("-minimize", Minimize);

            // -resolve
            commandLinedBuilder.AppendSwitchIfTrue("-resolve", Resolve);

            // -dump_exports
            commandLinedBuilder.AppendSwitchForFile("-dump_exports", DumpExports);

            // -dump_all
            commandLinedBuilder.AppendSwitchForFile("-dump_all", DumpAll);

            // -importResource
            commandLinedBuilder.AppendSwitchForEachFile("-importResource", ImportResources);

            // -compile
            commandLinedBuilder.AppendSwitchForFile("-compile", Compile);

            // -savestrings
            commandLinedBuilder.AppendSwitchForFile("-savestrings", SaveStrings);

            // -verbose
            commandLinedBuilder.AppendSwitchIfTrue("-verbose", Verbose);

            // -verboseMinimize
            commandLinedBuilder.AppendSwitchIfTrue("-verboseMinimize", VerboseMinimize);

            // -noByteCode
            commandLinedBuilder.AppendSwitchIfTrue("-noByteCode", NoByteCode);

            // -noAttributes
            commandLinedBuilder.AppendSwitchIfTrue("-noAttributes", NoAttributes);

            // -ignoreAssembly
            commandLinedBuilder.AppendSwitchForEachFile("-ignoreAssembly", IgnoreAssembly);

            // -generateStringsTable
            commandLinedBuilder.AppendSwitchForFile("-generateStringsTable", GenerateStringsTable);

            // -generate_dependency
            commandLinedBuilder.AppendSwitchForFile("-generate_dependency", GenerateDependency);

            // -create_database
            AppendCreateDatabase(commandLinedBuilder);

            // -generate_skeleton
            commandLinedBuilder.AppendSwitchToFileAndExtraSwitches("-generate_skeleton", GenerateSkeletonFile, GenerateSkeletonName, GenerateSkeletonProject);

            // -refresh_assembly
            AppendRefreshAssemblyCommand(commandLinedBuilder);

            // output command line for debug? 
            if (OutputCommandLine)
            {
                Log.LogWarning($"NFMDP cmd>> {GenerateFullPathToTool()} {commandLinedBuilder.ToString()} <<");
            }

            return commandLinedBuilder.ToString();
        }

        #region command line helper methods

        private void AppendLoadHints(CommandLineBuilder commandLinedBuilder)
        {
            LoadHints?.Select(hint => {

                commandLinedBuilder.AppendSwitch("-loadHints");
                commandLinedBuilder.AppendSwitch(Path.GetFileNameWithoutExtension(hint.GetMetadata("FullPath")));
                commandLinedBuilder.AppendFileNameIfNotNull(hint);

                return new object();
            }).ToList();
        }
        
        private void AppendCreateDatabase(CommandLineBuilder commandLinedBuilder)
        {
            if (CreateDatabase?.Length > 0)
            {
                if (this.CreateDatabase == null || this.CreateDatabase.Length == 0)
                    return;

                tempDataBaseFile = Path.GetTempFileName();
                using (StreamWriter sw = new StreamWriter(tempDataBaseFile))
                {
                    foreach (ITaskItem item in this.CreateDatabase)
                        sw.WriteLine(item.ItemSpec);
                }

                commandLinedBuilder.AppendSwitchToFileAndExtraSwitches("-create_database", tempDataBaseFile, CreateDatabaseFile);
            }
        }

        private void AppendRefreshAssemblyCommand(CommandLineBuilder commandLinedBuilder)
        {
            if (!string.IsNullOrEmpty(RefreshAssemblyName) && !string.IsNullOrEmpty(RefreshAssemblyOutput))
            {
                commandLinedBuilder.AppendSwitch("-refresh_assembly");
                commandLinedBuilder.AppendSwitch(RefreshAssemblyName);
                commandLinedBuilder.AppendFileNameIfNotNull(RefreshAssemblyOutput);
            }
        }

        #endregion
    }
}
