﻿//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.ComponentModel;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using nanoFramework.Tools.Utilities;
using nanoFramework.Tools.MetadataProcessor;
using System.Xml;
using nanoFramework.Tools.MetadataProcessor.Core;
using Mono.Cecil;

namespace nanoFramework.Tools
{
    [Description("MetaDataProcessorTaskEntry")]
    public class MetaDataProcessorTask : Task
    {

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

        /// <summary>
        /// Option to generate skeleton project without Interop support.
        /// This is required to generate Core Libraries.
        /// Default is false, meaning that Interop support will be used.
        /// </summary>
        public bool SkeletonWithoutInterop { get; set; } = false;

        public bool Resolve { get; set; }

        public string RefreshAssemblyName { get; set; }

        public string RefreshAssemblyOutput { get; set; }

        public string SaveStrings { get; set; }

        public string DumpAll { get; set; }

        public string DumpExports { get; set; }

        /// <summary>
        /// Sets whether the command line output is sent to the Log to help debugging command execution.
        /// Default is false.
        /// </summary>
        public bool OutputCommandLine { private get; set; } = false;

        private readonly List<ITaskItem> _filesWritten = new List<ITaskItem>();

        [Output]
        public ITaskItem[] FilesWritten { get { return _filesWritten.ToArray(); } }


        #endregion

        #region internal fields for MetadataProcessor

        private AssemblyDefinition _assemblyDefinition;
        private nanoAssemblyBuilder _assemblyBuilder;
        private readonly IDictionary<string, string> _loadHints =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _classNamesToExclude = new List<string>();

        #endregion


        public override bool Execute()
        {
            // report to VS output window what step the build is 
            Log.LogCommandLine(MessageImportance.Normal, "Starting nanoFramework MetadataProcessor...");

            /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // developer note: to debug this task set an environment variable like this:
            // set NFBUILD_TASKS_DEBUG=1
            // this will cause the execution to pause bellow so a debugger can be attached
            DebuggerHelper.WaitForDebuggerIfEnabled(TasksConstants.BuildTaskDebugVar);
            /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

            try
            {
                // execution of the metadata processor have to be carried in the appropriate order
                // failing to do so will most likely cause the task to fail

                // load hints for referenced assemblies
                if(LoadHints != null && 
                    LoadHints.Any())
                {
                    if(Verbose) Log.LogMessage(MessageImportance.Normal, "Processing load hints...");

                    foreach (var hint in LoadHints)
                    {
                        _loadHints[Path.GetFileNameWithoutExtension(hint.GetMetadata("FullPath"))] = hint.GetMetadata("FullPath");
                    }
                }

                // class names to exclude from processing
                if (ExcludeClassByName != null && 
                    ExcludeClassByName.Any())
                {
                    if (Verbose) Log.LogMessage(MessageImportance.Normal, "Processing class exclusion list...");

                    foreach (var className in ExcludeClassByName)
                    {
                        _classNamesToExclude.Add(className.ToString());
                    }
                }

                // Analyses a .NET assembly
                if (!string.IsNullOrEmpty(Parse))
                {
                    if (Verbose) Log.LogMessage(MessageImportance.Normal, $"Analysing .NET assembly {Path.GetFileNameWithoutExtension(Parse)}...");

                    ExecuteParse(Parse);
                }

                // compiles an assembly into nanoCLR format
                if (!string.IsNullOrEmpty(Compile))
                {
                    // sanity check for missing parse
                    if (string.IsNullOrEmpty(Parse))
                    {
                        // can't compile without analysing first
                        throw new ArgumentException("Can't compile without first analysing a .NET Assembly. Check the targets file for a missing option invoking MedataProcessor Task.");
                    }
                    else
                    {
                        if (Verbose) Log.LogCommandLine(MessageImportance.Normal, $"Compiling {Path.GetFileNameWithoutExtension(Compile)} into nanoCLR format...");

                        ExecuteCompile(Compile);
                    }
                }

                // generate skeleton files with stubs to add native code for an assembly
                if( !string.IsNullOrEmpty(GenerateSkeletonFile) &&
                    !string.IsNullOrEmpty(GenerateSkeletonProject) &&
                    !string.IsNullOrEmpty(GenerateSkeletonName))
                {
                    // sanity check for missing compile (therefore parse too)
                    if (string.IsNullOrEmpty(Compile))
                    {
                        // can't generate skeleton without compiling first
                        throw new ArgumentException("Can't generate skeleton project without first compiling the .NET Assembly. Check the targets file for a missing option invoking MedataProcessor Task.");
                    }
                    else
                    {
                        if (Verbose) Log.LogMessage(MessageImportance.Normal, $"Generating skeleton...");

                        ExecuteGenerateSkeleton(
                            GenerateSkeletonFile,
                            GenerateSkeletonName,
                            GenerateSkeletonProject,
                            SkeletonWithoutInterop);
                    }
                }

                RecordFilesWritten();
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex, true);
            }

            // if we've logged any errors that's because there were errors (WOW!)
            return !Log.HasLoggedErrors;
        }

        private void RecordFileWritten(
            string file)
        {
            if (!string.IsNullOrEmpty(file))
            {
                if (File.Exists(file))
                {
                    _filesWritten.Add(new TaskItem(file));
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

        #region Metadata Processor helper methods

        private void ExecuteParse(
            string fileName)
        {
            try
            {
                if (Verbose) System.Console.WriteLine("Parsing assembly...");

                _assemblyDefinition = AssemblyDefinition.ReadAssembly(fileName,
                    new ReaderParameters { AssemblyResolver = new LoadHintsAssemblyResolver(_loadHints) });
            }
            catch (Exception)
            {
                Log.LogError($"Unable to parse input assembly file '{fileName}' - check if path and file exists.");
            }
        }

        private void ExecuteCompile(
            string fileName)
        {
            try
            {
                if (Verbose) System.Console.WriteLine("Compiling assembly...");

                _assemblyBuilder = new nanoAssemblyBuilder(_assemblyDefinition, _classNamesToExclude, Minimize, Verbose);

                using (var stream = File.Open(fileName, FileMode.Create, FileAccess.ReadWrite))
                using (var writer = new BinaryWriter(stream))
                {
                    _assemblyBuilder.Write(GetBinaryWriter(writer));
                }

                using (var writer = XmlWriter.Create(Path.ChangeExtension(fileName, "pdbx")))
                {
                    _assemblyBuilder.Write(writer);
                }
            }
            catch (Exception)
            {
                Log.LogError($"Unable to compile output assembly file '{fileName}' - check parse command results.");

                throw;
            }
        }

        private void AddClassToExclude(
            string className)
        {
            _classNamesToExclude.Add(className);
        }

        private void ExecuteGenerateSkeleton(
            string file,
            string name,
            string project,
            bool withoutInteropCode)
        {
            try
            {
                if (!withoutInteropCode)
                {
                    throw new ArgumentException("Generator for Interop stubs is not supported yet.");
                }

                if (Verbose) Log.LogMessage(MessageImportance.Normal, "Generating skeleton files...");

                var skeletonGenerator = new nanoSkeletonGenerator(
                    _assemblyBuilder.TablesContext,
                    file,
                    name,
                    project,
                    withoutInteropCode);

                skeletonGenerator.GenerateSkeleton();
            }
            catch (Exception)
            {
                Log.LogError("Unable to generate skeleton files");

                throw;
            }
        }

        private void ExecuteGenerateDependency(
            string fileName)
        {
            try
            {
                var dependencyGenerator = new nanoDependencyGenerator(
                    _assemblyDefinition,
                    _assemblyBuilder.TablesContext,
                    fileName);

                using (var writer = XmlWriter.Create(fileName))
                {
                    dependencyGenerator.Write(writer);
                }
            }
            catch (Exception)
            {
                Log.LogError($"Unable to generate and write dependency graph for assembly file '{fileName}'.");

                throw;
            }
        }

        private nanoBinaryWriter GetBinaryWriter(
            BinaryWriter writer)
        {
            return nanoBinaryWriter.CreateLittleEndianBinaryWriter(writer);
        }

        #endregion

    }
}
