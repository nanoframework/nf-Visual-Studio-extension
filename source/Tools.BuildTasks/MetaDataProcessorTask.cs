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

        public ITaskItem[] Load { get; set; }

        public ITaskItem[] LoadDatabase { get; set; }

        public ITaskItem[] LoadStrings { get; set; }

        public ITaskItem[] ExcludeClassByName { get; set; }

        public ITaskItem[] Parse { get; set; }

        public bool Minimize { get; set; }

        public string Compile { get; set; }

        public ITaskItem[] ImportResources { get; set; }

        [Output]
        public ITaskItem[] FilesWritten { get { return _FilesWritten.ToArray(); } private set { } }
        private List<ITaskItem> _FilesWritten = new List<ITaskItem>();

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
            //RecordFileWritten(SaveStrings);
            //RecordFileWritten(GenerateStringsTable);
            //RecordFileWritten(DumpAll);
            //RecordFileWritten(DumpExports);
            RecordFileWritten(Compile);
            RecordFileWritten(Path.ChangeExtension(Compile, "pdbx"));
            //RecordFileWritten(RefreshAssemblyOutput);
            //RecordFileWritten(CreateDatabaseFile);
            //RecordFileWritten(GenerateDependency);
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
            commandLinedBuilder.AppendSwitchAndFiles("-loadStrings", LoadStrings);

            // -excludeClassByName
            commandLinedBuilder.AppendSwitchForEachFile("-excludeClassByName", ExcludeClassByName);

            // -parse
            commandLinedBuilder.AppendSwitchAndFiles("-parse", Parse);

            // -minimize
            commandLinedBuilder.AppendSwitchIfTrue("-minimize", Minimize);

            //AppendSwitchIfTrue(commandLine, "-resolve", this.Resolve);

            //AppendSwitchFiles(commandLine, "-dump_exports", this.DumpExports);

            //AppendSwitchFiles(commandLine, "-dump_all", this.DumpAll);

            //AppendSwitch(commandLine, "-importResource", this.ImportResources);

            // -compile
            commandLinedBuilder.AppendSwitchForFile("-compile", Compile);

            //AppendSwitchFiles(commandLine, "-savestrings", this.SaveStrings);

            //AppendSwitchIfTrue(commandLine, "-verbose", this.Verbose);

            //AppendSwitchIfTrue(commandLine, "-verboseMinimize", this.VerboseMinimize);

            //AppendSwitchIfTrue(commandLine, "-noByteCode", this.NoByteCode);

            //AppendSwitchIfTrue(commandLine, "-noAttributes", this.NoAttributes);

            //AppendSwitch(commandLine, "-ignoreAssembly", this.IgnoreAssembly);

            //AppendSwitchFiles(commandLine, "-generateStringsTable", this.GenerateStringsTable);

            //AppendSwitchFiles(commandLine, "-generate_dependency", this.GenerateDependency);

            //AppendCreateDatabase(commandLine);

            //AppendSwitchFileStrings(commandLine, "-generate_skeleton", this.GenerateSkeletonFile, this.GenerateSkeletonName, this.GenerateSkeletonProject, this.LegacySkeletonInterop ? "TRUE" : "FALSE");

            //AppendRefreshAssemblyCommand(commandLine);



            //commandLinedBuilder.AppendSwitch("-parse");
            //commandLinedBuilder.AppendFileNameIfNotNull(@"C:\Users\jassimoes\Documents\Visual Studio 2017\Projects\NFApp111\NFApp111\bin\Debug\NFApp111.exe");


            //Log.LogWarning("cmd: " + commandLinedBuilder.ToString());

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
        

        private void AppendCreateDatabase(CommandLineBuilder commandLine)
        {
            //if (this.CreateDatabase == null || this.CreateDatabase.Length == 0)
            //    return;

            //tempDataBaseFile = Path.GetTempFileName();
            //using (StreamWriter sw = new StreamWriter(m_tempFile))
            //{
            //    foreach (ITaskItem item in this.CreateDatabase)
            //        sw.WriteLine(GetProperBuildFlavor(item.ItemSpec));
            //}

            //AppendSwitchFiles(commandLine, "-create_database", tempDataBaseFile, this.CreateDatabaseFile);
        }

        #endregion
    }
}
