//
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

        public string GenerateStringsTable { get; set; }

        public string Compile { get; set; }

        public bool Verbose { get; set; }

        public bool VerboseMinimize { get; set; }

        public bool NoByteCode { get; set; }

        public bool NoAttributes { get; set; }

        public ITaskItem[] CreateDatabase { get; set; }

        /// <summary>
        /// Parameter to enable stubs generation step.
        /// </summary>
        public bool GenerateStubs { get; set; } = false;

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

        public string SaveStrings { get; set; }

        public bool DumpMetadata { get; set; }

        public string DumpFile { get; set; }

        public string DumpExports { get; set; }

        /// <summary>
        /// Flag to set when compiling a Core Library.
        /// </summary>
        public bool IsCoreLibrary { get; set; } = false;

        private readonly List<ITaskItem> _filesWritten = new List<ITaskItem>();

        [Output]
        public ITaskItem[] FilesWritten { get { return _filesWritten.ToArray(); } }

        [Output]
        public ITaskItem NativeChecksum { get { return new TaskItem(_nativeChecksum); } }

        #endregion

        #region internal fields for MetadataProcessor

        private AssemblyDefinition _assemblyDefinition;
        private nanoAssemblyBuilder _assemblyBuilder;
        private readonly IDictionary<string, string> _loadHints =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _classNamesToExclude = new List<string>();
        private string _nativeChecksum = "";

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
                    if(Verbose) Log.LogCommandLine(MessageImportance.Normal, "Processing load hints...");

                    foreach (var hint in LoadHints)
                    {
                        var assemblyName = Path.GetFileNameWithoutExtension(hint.GetMetadata("FullPath"));
                        var assemblyPath = hint.GetMetadata("FullPath");

                        _loadHints[assemblyName] = assemblyPath;

                        if (Verbose) Log.LogCommandLine(MessageImportance.Normal, $"Adding load hint: {assemblyName} @ '{assemblyPath}'");
                    }
                }

                // class names to exclude from processing
                if (ExcludeClassByName != null && 
                    ExcludeClassByName.Any())
                {
                    if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Processing class exclusion list...");

                    foreach (var className in ExcludeClassByName)
                    {
                        _classNamesToExclude.Add(className.ToString());

                        if (Verbose) Log.LogCommandLine(MessageImportance.Normal, $"Adding '{className.ToString()}' to collection of classes to exclude");
                    }
                }

                // Analyses a .NET assembly
                if (!string.IsNullOrEmpty(Parse))
                {
                    if (Verbose) Log.LogCommandLine(MessageImportance.Normal, $"Analysing .NET assembly {Path.GetFileNameWithoutExtension(Parse)}...");

                    ExecuteParse(Parse);
                }

                // compiles an assembly into nanoCLR format
                if (!string.IsNullOrEmpty(Compile))
                {
                    // sanity check for missing parse
                    if (string.IsNullOrEmpty(Parse))
                    {
                        // can't compile without analysing first
                        throw new ArgumentException("Can't compile without first analysing a .NET Assembly. Check the targets file for a missing option invoking MetadataProcessor Task.");
                    }
                    else
                    {
                        if (Verbose) Log.LogCommandLine(MessageImportance.Normal, $"Compiling {Path.GetFileNameWithoutExtension(Compile)} into nanoCLR format...");

                        ExecuteCompile(Compile);
                    }
                }

                // generate skeleton files with stubs to add native code for an assembly
                if (GenerateStubs)
                {
                    if(string.IsNullOrEmpty(GenerateSkeletonFile))
                    {
                        // can't generate skeleton without GenerateSkeletonFile parameter
                        throw new ArgumentException("Can't generate skeleton project without 'GenerateSkeletonFile'. Check the targets file for a missing parameter when invoking MetadataProcessor Task.");
                    }

                    if (string.IsNullOrEmpty(GenerateSkeletonProject))
                    {
                        // can't generate skeleton without GenerateSkeletonProject parameter
                        throw new ArgumentException("Can't generate skeleton project without 'GenerateSkeletonProject'. Check the targets file for a missing parameter when invoking MetadataProcessor Task.");
                    }

                    if (string.IsNullOrEmpty(GenerateSkeletonName))
                    {
                        // can't generate skeleton without GenerateSkeletonName parameter
                        throw new ArgumentException("Can't generate skeleton project without 'GenerateSkeletonName'. Check the targets file for a missing parameter when invoking MetadataProcessor Task.");
                    }

                    // sanity check for missing compile (therefore parse too)
                    if (string.IsNullOrEmpty(Compile))
                    {
                        // can't generate skeleton without compiling first
                        throw new ArgumentException("Can't generate skeleton project without first compiling the .NET Assembly. Check the targets file for a missing option invoking MetadataProcessor Task.");
                    }
                    else
                    {
                        if (Verbose) Log.LogCommandLine(MessageImportance.Normal, $"Generating skeleton '{GenerateSkeletonName}' for {GenerateSkeletonProject} \r\nPlacing files @ '{GenerateSkeletonFile}'");

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
            finally
            {
                // need to dispose the AssemblyDefinition before leaving because Mono.Cecil assembly loading and resolution
                // operations leave the assembly file locked in the AppDomain preventing it from being open on subsequent Tasks
                // see https://github.com/nanoframework/Home/issues/553
                if (_assemblyDefinition != null)
                {
                    _assemblyDefinition.Dispose();
                }
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
            RecordFileWritten(DumpFile);
            RecordFileWritten(DumpExports);
            RecordFileWritten(Compile);
            RecordFileWritten(Path.ChangeExtension(Compile, "pdbx"));
            RecordFileWritten(CreateDatabaseFile);
            RecordFileWritten(GenerateDependency);
        }

        #region Metadata Processor helper methods

        private void ExecuteParse(
            string fileName)
        {
            try
            {
                if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Parsing assembly...");

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
                // compile assembly (1st pass)
                if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Compiling assembly...");

                _assemblyBuilder = new nanoAssemblyBuilder(
                    _assemblyDefinition,
                    _classNamesToExclude,
                    Verbose,
                    IsCoreLibrary);

                using (var stream = File.Open(Path.ChangeExtension(fileName, "tmp"), FileMode.Create, FileAccess.ReadWrite))
                using (var writer = new BinaryWriter(stream))
                {
                    _assemblyBuilder.Write(GetBinaryWriter(writer));
                }
            }
            catch (Exception)
            {
                Log.LogError($"Unable to compile output assembly file '{fileName}' - check parse command results.");

                throw;
            }

            try
            {
                // OK to delete tmp PE file
                File.Delete(Path.ChangeExtension(fileName, "tmp"));

                // minimize (has to be called after the 1st compile pass)
                if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Minimizing assembly...");

                _assemblyBuilder.Minimize();

                // compile assembly (2nd pass after minimize)
                if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Recompiling assembly...");

                using (var stream = File.Open(fileName, FileMode.Create, FileAccess.ReadWrite))
                using (var writer = new BinaryWriter(stream))
                {
                    _assemblyBuilder.Write(GetBinaryWriter(writer));
                }

                // output PDBX
                using (var writer = XmlWriter.Create(Path.ChangeExtension(fileName, "pdbx")))
                {
                    _assemblyBuilder.Write(writer);
                }

                // output assembly metadata
                if (DumpMetadata)
                {
                    if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Dumping assembly metadata...");

                    DumpFile = Path.ChangeExtension(fileName, "dump.txt");

                    nanoDumperGenerator dumper = new nanoDumperGenerator(
                        _assemblyBuilder.TablesContext,
                        DumpFile);
                    dumper.DumpAll();
                }

                // set environment variable with assembly native checksum
                Environment.SetEnvironmentVariable("AssemblyNativeChecksum", _assemblyBuilder.GetNativeChecksum(), EnvironmentVariableTarget.Process);

                // store assembly native checksum
                _nativeChecksum = _assemblyBuilder.GetNativeChecksum();
            }
            catch (ArgumentException ex)
            {
                Log.LogError($"Exception minimizing assembly: {ex.Message}.");
            }
            catch (Exception)
            {
                Log.LogError($"Exception minimizing assembly.");
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
                if (Verbose) Log.LogCommandLine(MessageImportance.Normal, "Generating skeleton files...");

                var skeletonGenerator = new nanoSkeletonGenerator(
                    _assemblyBuilder.TablesContext,
                    file,
                    name,
                    project,
                    withoutInteropCode,
                    IsCoreLibrary);

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
