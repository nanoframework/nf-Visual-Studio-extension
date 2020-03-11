//
// Copyright (c) The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using nanoFramework.Tools.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security;
using System.Xml;

namespace nanoFramework.Tools
{
    static internal class ExceptionHandling
    {
        /// <summary>
        /// If the given exception is file IO related then return.
        /// Otherwise, rethrow the exception.
        /// </summary>
        /// <param name="e">The exception to check.</param>
        internal static void RethrowUnlessFileIO(Exception e)
        {
            if
            (
                e is UnauthorizedAccessException
                || e is ArgumentNullException
                || e is PathTooLongException
                || e is DirectoryNotFoundException
                || e is NotSupportedException
                || e is ArgumentException
                || e is SecurityException
                || e is IOException
            )
            {
                return;
            }

            Debug.Assert(false, "Exception unexpected for this File IO case. Please open a bug that we need to add a 'catch' block for this exception. Look at the build log for more details including a stack trace.");
            throw e;
        }
    }


    /// <remarks>
    /// Represents a cache of inputs to a compilation-style task.
    /// </remarks>
    [Serializable()]
    internal class Dependencies
    {
        /// <summary>
        /// Hashtable of other dependency files.
        /// Key is filename and value is DependencyFile.
        /// </summary>
        private Hashtable dependencies = new Hashtable();

        /// <summary>
        /// Look up a dependency file. Return null if its not there.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        internal DependencyFile GetDependencyFile(string filename)
        {
            return (DependencyFile)dependencies[filename];
        }


        /// <summary>
        /// Add a new dependency file.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        internal void AddDependencyFile(string filename, DependencyFile file)
        {
            dependencies[filename] = file;
        }

        /// <summary>
        /// Remove new dependency file.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        internal void RemoveDependencyFile(string filename)
        {
            dependencies.Remove(filename);
        }

        /// <summary>
        /// Remove all entries from the dependency table.
        /// </summary>
        internal void Clear()
        {
            dependencies.Clear();
        }
    }


    /// <remarks>
    /// Represents a single input to a compilation-style task.
    /// Keeps track of timestamp for later comparison.
    /// </remarks>
    [Serializable]
    internal class DependencyFile
    {

        // Whether the file exists or not.
        readonly bool exists = false;

        /// <summary>
        /// The name of the file.
        /// </summary>
        /// <value></value>
        internal string FileName { get; }

        /// <summary>
        /// The last-modified timestamp when the class was instantiated.
        /// </summary>
        /// <value></value>
        internal DateTime LastModified { get; }

        /// <summary>
        /// Returns true if the file existed when this class was instantiated.
        /// </summary>
        /// <value></value>
        internal bool Exists
        {
            get { return exists; }
        }

        /// <summary>
        /// Construct.
        /// </summary>
        /// <param name="filename">The file name.</param>
        internal DependencyFile(string filename)
        {
            FileName = filename;

            if (File.Exists(FileName))
            {
                LastModified = File.GetLastWriteTime(FileName);
                exists = true;
            }
            else
            {
                exists = false;
            }
        }

        /// <summary>
        /// Checks whether the file has changed since the last time a timestamp was recorded.
        /// </summary>
        /// <returns></returns>
        internal bool HasFileChanged()
        {
            // Obviously if the file no longer exists then we are not up to date.
            if (!File.Exists(FileName))
            {
                return true;
            }

            // Check the saved timestamp against the current timestamp.
            // If they are different then obviously we are out of date.
            DateTime curLastModified = File.GetLastWriteTime(FileName);
            if (curLastModified != LastModified)
            {
                return true;
            }

            // All checks passed -- the info should still be up to date.
            return false;
        }
    }

    /// <remarks>
    /// Base class for task state files.
    /// </remarks>
    [Serializable()]
    internal class StateFileBase
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        internal StateFileBase()
        {
            // do nothing
        }

        /// <summary>
        /// Writes the contents of this object out to the specified file.
        /// </summary>
        /// <param name="stateFile"></param>
        virtual internal void SerializeCache(string stateFile, TaskLoggingHelper log)
        {
            try
            {
                if (stateFile != null && stateFile.Length > 0)
                {
                    if (File.Exists(stateFile))
                    {
                        File.Delete(stateFile);
                    }

                    using (FileStream s = new FileStream(stateFile, FileMode.CreateNew))
                    {
                        BinaryFormatter formatter = new BinaryFormatter();
                        formatter.Serialize(s, this);
                    }
                }
            }
            catch (Exception e)
            {
                // If there was a problem writing the file (like it's read-only or locked on disk, for
                // example), then eat the exception and log a warning.  Otherwise, rethrow.
                ExceptionHandling.RethrowUnlessFileIO(e);

                // Not being able to serialize the cache is not an error, but we let the user know anyway.
                // Don't want to hold up processing just because we couldn't read the file.
                log.LogWarning("Could not write state file {0} ({1})", stateFile, e.Message);
            }
        }

        /// <summary>
        /// Reads the specified file from disk into a StateFileBase derived object.
        /// </summary>
        /// <param name="stateFile"></param>
        /// <returns></returns>
        static internal StateFileBase DeserializeCache(string stateFile, TaskLoggingHelper log, Type requiredReturnType)
        {
            StateFileBase retVal = null;

            // First, we read the cache from disk if one exists, or if one does not exist
            // then we create one.
            try
            {
                if (stateFile != null && stateFile.Length > 0 && File.Exists(stateFile))
                {
                    using (FileStream s = new FileStream(stateFile, FileMode.Open))
                    {
                        BinaryFormatter formatter = new BinaryFormatter();
                        retVal = (StateFileBase)formatter.Deserialize(s);

                        if ((retVal != null) && (!requiredReturnType.IsInstanceOfType(retVal)))
                        {
                            log.LogWarning("Could not write state file {0} (Incompatible state file type)", stateFile);
                            retVal = null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // The deserialization process seems like it can throw just about
                // any exception imaginable.  Catch them all here.

                // Not being able to deserialize the cache is not an error, but we let the user know anyway.
                // Don't want to hold up processing just because we couldn't read the file.
                log.LogWarning("Could not read state file {0} ({1})", stateFile, e.Message);
            }

            return retVal;
        }

        /// <summary>
        /// Deletes the state file from disk
        /// </summary>
        /// <param name="stateFile"></param>
        /// <param name="log"></param>
        static internal void DeleteFile(string stateFile, TaskLoggingHelper log)
        {
            try
            {
                if (stateFile != null && 
                    stateFile.Length > 0 &&
                    File.Exists(stateFile))
                {
                    File.Delete(stateFile);
                }
            }
            catch (Exception e)
            {
                // If there was a problem deleting the file (like it's read-only or locked on disk, for
                // example), then eat the exception and log a warning.  Otherwise, rethrow.
                ExceptionHandling.RethrowUnlessFileIO(e);

                log.LogWarning("Could not delete state file {0} ({1})", stateFile, e.Message);
            }
        }
    }


    /// <remarks>
    /// This class is a caching mechanism for the resgen task to keep track of linked
    /// files within processed .resx files.
    /// </remarks>
    [Serializable()]
    internal sealed class ResGenDependencies : StateFileBase
    {
        /// <summary>
        /// The list of resx files.
        /// </summary>
        private Dependencies resXFiles = new Dependencies();

        /// <summary>
        /// A newly-created ResGenDependencies is not dirty.
        /// What would be the point in saving the default?
        /// </summary>
        [NonSerialized]
        private bool isDirty = false;

        /// <summary>
        ///  This is the directory that will be used for resolution of files linked within a .resx.
        ///  If this is NULL then we use the directory in which the .resx is in (that should always
        ///  be the default!)
        /// </summary>
        private string baseLinkedFileDirectory;

        /// <summary>
        /// Construct.
        /// </summary>
        internal ResGenDependencies()
        {
        }

        internal string BaseLinkedFileDirectory
        {
            get
            {
                return baseLinkedFileDirectory;
            }
            set
            {
                if (value == null && baseLinkedFileDirectory == null)
                {
                    // No change
                }
                else if ((value == null && baseLinkedFileDirectory != null) ||
                         (value != null && baseLinkedFileDirectory == null) ||
                         (String.Compare(baseLinkedFileDirectory, value, true, CultureInfo.InvariantCulture) != 0))
                {
                    // Ok, this is slightly complicated.  Changing the base directory in any manner may
                    // result in changes to how we find .resx files.  Therefore, we must clear our out
                    // cache whenever the base directory changes.
                    resXFiles.Clear();
                    isDirty = true;
                    baseLinkedFileDirectory = value;
                }
            }
        }

        internal bool UseSourcePath
        {
            set
            {
                // Ensure that the cache is properly initialized with respect to how resgen will
                // resolve linked files within .resx files.  ResGen has two different
                // ways for resolving relative file-paths in linked files. The way
                // that ResGen resolved relative paths before Whidbey was always to
                // resolve from the current working directory. In Whidbey a new command-line
                // switch "/useSourcePath" instructs ResGen to use the folder that
                // contains the .resx file as the path from which it should resolve
                // relative paths. So we should base our timestamp/existence checking
                // on the same switch & resolve in the same manner as ResGen.
                BaseLinkedFileDirectory = value ? null : Environment.CurrentDirectory;
            }
        }

        internal ResXFile GetResXFileInfo(string resxFile)
        {
            // First, try to retrieve the resx information from our hashtable.
            ResXFile retVal = (ResXFile)resXFiles.GetDependencyFile(resxFile);

            if (retVal == null)
            {
                // Ok, the file wasn't there.  Add it to our cache and return it to the caller.
                retVal = AddResxFile(resxFile);
            }
            else
            {
                // The file was there.  Is it up to date?  If not, then we'll have to refresh the file
                // by removing it from the hashtable and readding it.
                if (retVal.HasFileChanged())
                {
                    resXFiles.RemoveDependencyFile(resxFile);
                    isDirty = true;
                    retVal = AddResxFile(resxFile);
                }
            }

            return retVal;
        }

        private ResXFile AddResxFile(string file)
        {
            // This method adds a .resx file "file" to our .resx cache.  The method causes the file
            // to be cracked for contained files.

            ResXFile resxFile = new ResXFile(file, BaseLinkedFileDirectory);
            resXFiles.AddDependencyFile(file, resxFile);
            isDirty = true;
            return resxFile;
        }
        /// <summary>
        /// Writes the contents of this object out to the specified file.
        /// </summary>
        /// <param name="stateFile"></param>
        override internal void SerializeCache(string stateFile, TaskLoggingHelper log)
        {
            base.SerializeCache(stateFile, log);
            isDirty = false;
        }

        /// <summary>
        /// Reads the .cache file from disk into a ResGenDependencies object.
        /// </summary>
        /// <param name="stateFile"></param>
        /// <param name="useSourcePath"></param>
        /// <returns></returns>
        internal static ResGenDependencies DeserializeCache(string stateFile, bool useSourcePath, TaskLoggingHelper log)
        {
            ResGenDependencies retVal = (ResGenDependencies)StateFileBase.DeserializeCache(stateFile, log, typeof(ResGenDependencies));

            if (retVal == null)
            {
                retVal = new ResGenDependencies();
            }

            // Ensure that the cache is properly initialized with respect to how resgen will
            // resolve linked files within .resx files.  ResGen has two different
            // ways for resolving relative file-paths in linked files. The way
            // that ResGen resolved relative paths before Whidbey was always to
            // resolve from the current working directory. In Whidbey a new command-line
            // switch "/useSourcePath" instructs ResGen to use the folder that
            // contains the .resx file as the path from which it should resolve
            // relative paths. So we should base our timestamp/existence checking
            // on the same switch & resolve in the same manner as ResGen.
            retVal.UseSourcePath = useSourcePath;

            return retVal;
        }

        /// <remarks>
        /// Represents a single .resx file in the dependency cache.
        /// </remarks>
        [Serializable()]
        internal sealed class ResXFile : DependencyFile
        {
            internal string[] LinkedFiles { get; }

            internal ResXFile(string filename, string baseLinkedFileDirectory)
                : base(filename)
            {
                // Creates a new ResXFile object and populates the class member variables
                // by computing a list of linked files within the .resx that was passed in.
                //
                // filename is the filename of the .resx file that is to be examined.

                if (File.Exists(FileName))
                {
                    LinkedFiles = ResXFile.GetLinkedFiles(filename, baseLinkedFileDirectory);
                }
            }

            /// <summary>
            /// Given a .RESX file, returns all the linked files that are referenced within that .RESX.
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="baseLinkedFileDirectory"></param>
            /// <returns></returns>
            /// <exception cref="ArgumentException">May be thrown if Resx is invalid. May contain XmlException.</exception>
            /// <exception cref="XmlException">May be thrown if Resx is invalid</exception>
            internal static string[] GetLinkedFiles(string filename, string baseLinkedFileDirectory)
            {
                // This method finds all linked .resx files for the .resx file that is passed in.
                // filename is the filename of the .resx file that is to be examined.

                // Construct the return array
                ArrayList retVal = new ArrayList();

#if !EVERETT_BUILD
                using (ResXResourceReader resxReader = new ResXResourceReader(filename))
                {
                    // Tell the reader to return ResXDataNode's instead of the object type
                    // the resource becomes at runtime so we can figure out which files
                    // the .resx references
                    resxReader.UseResXDataNodes = true;

                    // First we need to figure out where the linked file resides in order
                    // to see if it exists & compare its timestamp, and we need to do that
                    // comparison in the same way ResGen does it. ResGen has two different
                    // ways for resolving relative file-paths in linked files. The way
                    // that ResGen resolved relative paths before Whidbey was always to
                    // resolve from the current working directory. In Whidbey a new command-line
                    // switch "/useSourcePath" instructs ResGen to use the folder that
                    // contains the .resx file as the path from which it should resolve
                    // relative paths. So we should base our timestamp/existence checking
                    // on the same switch & resolve in the same manner as ResGen.
                    resxReader.BasePath = baseLinkedFileDirectory ?? Path.GetDirectoryName(filename);

                    foreach (DictionaryEntry dictEntry in resxReader)
                    {
                        if (dictEntry.Value is ResXDataNode)
                        {
                            ResXFileRef resxFileRef = ((ResXDataNode)dictEntry.Value).FileRef;
                            if (resxFileRef != null)
                                retVal.Add(resxFileRef.FileName);
                        }
                    }
                }
#endif
                return (string[])retVal.ToArray(typeof(string));
            }
        }

        /// <summary>
        /// Whether this cache is dirty or not.
        /// </summary>
        internal bool IsDirty
        {
            get
            {
                return isDirty;
            }
        }
    }

    [Description("GenerateNanoResourceTaskEntry")]
    public class GenerateNanoResourceTask : Task
    {
        /// <summary>
        /// List of output files that we failed to create due to an error.
        /// See note in RemoveUnsuccessfullyCreatedResourcesFromOutputResources()
        /// </summary>
        private readonly List<string> _unsuccessfullyCreatedOutFiles = new List<string>();

        // This cache helps us track the linked resource files listed inside of a resx resource file
        private ResGenDependencies cache;

        #region public properties for the task

        /// <summary>
        /// The names of the items to be converted. The extension must be one of the
        //  following: .txt, .resx or .resources.
        /// </summary>
        [Required]
        public ITaskItem[] Sources { get; set; }

        /// <summary>
        /// Indicates whether the resource reader should use the source file's directory to
        /// resolve relative file paths.
        /// </summary>
        public bool UseSourcePath { get; set; }

        /// <summary>
        /// Resolves types in ResX files (XML resources) for Strongly Typed Resources
        /// </summary>
        public ITaskItem[] References { get; set; }

        /// <summary>
        /// This is the path/name of the file containing the dependency cache
        /// </summary>
        public ITaskItem StateFile { get; set; }

        /// <summary>
        /// The name(s) of the resource file to create. If the user does not specify this
        /// attribute, the task will append a .resources extension to each input filename
        /// argument and write the file to the directory that contains the input file.
        /// Includes any output files that were already up to date, but not any output files
        /// that failed to be written due to an error.
        /// </summary>
        [Output]
        public ITaskItem[] OutputResources { get; set; }

        /// <summary>
        /// Storage for names of *all files* written to disk.  This is part of the implementation
        /// for Clean, and contains the OutputResources items and the StateFile item.
        /// Includes any output files that were already up to date, but not any output files
        /// that failed to be written due to an error.
        /// </summary>
        [Output]
        public ITaskItem[] FilesWritten { get { return _filesWritten.ToArray(); } }
        private readonly List<ITaskItem> _filesWritten = new List<ITaskItem>();

        /// <summary>
        /// (default = false)
        /// When true, a new AppDomain is always created to evaluate the .resx files.
        /// When false, a new AppDomain is created only when it looks like a user's
        ///  assembly is referenced by the .resx.
        /// </summary>
        public bool NeverLockTypeAssemblies { get; set; }
        

        #endregion

        public override bool Execute()
        {
            // report to VS output window what step the build is 
            Log.LogMessage(MessageImportance.Normal, "Generating nanoResources nanoFramework assembly...");

            // wait for debugger on var
            DebuggerHelper.WaitForDebuggerIfEnabled(TasksConstants.BuildTaskDebugVar);

            try
            {
                // If there are no sources to process, just return (with success) and report the condition.
                if ((Sources == null) || (Sources.Length == 0))
                {
                    Log.LogMessage(MessageImportance.Low, "GenerateResource.NoSources");
                    
                    // Indicate we generated nothing
                    OutputResources = null;

                    return true;
                }

                if (!ValidateParameters())
                {
                    // Indicate we generated nothing
                    OutputResources = null;
                    return false;
                }

                // In the case that OutputResources wasn't set, build up the outputs by transforming the Sources
                if (!CreateOutputResourcesNames())
                {
                    // Indicate we generated nothing
                    OutputResources = null;
                    return false;
                }

                // First we look to see if we have a resgen linked files cache.  If so, then we can use that
                // cache to speed up processing.
                ReadStateFile();

                bool nothingOutOfDate = true;

                List<ITaskItem> inputsToProcess = new List<ITaskItem>();
                List<ITaskItem> outputsToProcess = new List<ITaskItem>();

                // decide what sources we need to build
                for (int i = 0; i < Sources.Length; ++i)
                {
                    // Attributes from input items are forwarded to output items.

                    if (!File.Exists(Sources[i].ItemSpec))
                    {
                        // Error but continue with the files that do exist
                        Log.LogError("GenerateResource.ResourceNotFound", Sources[i].ItemSpec);
                        _unsuccessfullyCreatedOutFiles.Add(OutputResources[i].ItemSpec);
                    }
                    else
                    {
                        // check to see if the output resources file (and, if it is a .resx, any linked files)
                        // is up to date compared to the input file
                        if (ShouldRebuildResgenOutputFile(Sources[i].ItemSpec, OutputResources[i].ItemSpec))
                        {
                            nothingOutOfDate = false;

                            inputsToProcess.Add(Sources[i]);
                            outputsToProcess.Add(OutputResources[i]);
                        }
                    }
                }

                if (nothingOutOfDate)
                {
                    Log.LogMessage("GenerateResource.NothingOutOfDate");
                }
                else
                {
                    // Prepare list of referenced assemblies
                    AssemblyName[] assemblyList;
                    try
                    { //only load system.drawing, mscorlib.  no parameters needed here?!!
                        assemblyList = LoadReferences();
                    }
                    catch (ArgumentException e)
                    {
                        Log.LogError("GenerateResource.ReferencedAssemblyNotFound - {0}: {1}", e.ParamName, e.Message);
                        OutputResources = null;
                        return false;
                    }

                    // always create a separate AppDomain because an assembly would be locked.
                    AppDomain appDomain = null;

                    ProcessResourceFiles process = null;

                    try
                    {
                        appDomain = AppDomain.CreateDomain
                        (
                            "generateResourceAppDomain",
                            null,
                            AppDomain.CurrentDomain.SetupInformation
                        );

                        object obj = appDomain.CreateInstanceFromAndUnwrap
                            (
                                typeof(ProcessResourceFiles).Module.FullyQualifiedName,
                                typeof(ProcessResourceFiles).FullName
                            );

                        process = (ProcessResourceFiles)obj;

                        //setup strongly typed class name??

                        process.Run(Log, assemblyList, inputsToProcess.ToArray(), outputsToProcess.ToArray(),
                            UseSourcePath);

                        if (null != process.UnsuccessfullyCreatedOutFiles)
                        {
                            foreach (string item in process.UnsuccessfullyCreatedOutFiles)
                            {
                                _unsuccessfullyCreatedOutFiles.Add(item);
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
                }

                // And now we serialize the cache to save our resgen linked file resolution for later use.
                WriteStateFile();

                RemoveUnsuccessfullyCreatedResourcesFromOutputResources();

                RecordFilesWritten();
            }
            catch (Exception ex)
            {
                Log.LogError("nanoFramework GenerateNanoResourceTask error: " + ex.Message);
            }

            // if we've logged any errors that's because there were errors (WOW!)
            return !Log.HasLoggedErrors;
        }

        /// <summary>
        /// Check for parameter errors.
        /// </summary>
        /// <returns>true if parameters are valid</returns>
        private bool ValidateParameters()
        {
            // make sure that if the output resources were set, they exactly match the number of input sources
            if ((OutputResources != null) && (OutputResources.Length != Sources.Length))
            {
                Log.LogError("General.TwoVectorsMustHaveSameLength", Sources.Length, OutputResources.Length, "Sources", "OutputResources");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Make sure that OutputResources has 1 file name for each name in Sources.
        /// </summary>
        private bool CreateOutputResourcesNames()
        {
            if (OutputResources == null)
            {
                OutputResources = new ITaskItem[Sources.Length];

                int i = 0;
                try
                {
                    for (i = 0; i < Sources.Length; ++i)
                    {
                        OutputResources[i] = new TaskItem(Path.ChangeExtension(Sources[i].ItemSpec, ".nanoresources"));
                    }
                }
                catch (ArgumentException e)
                {
                    Log.LogError("GenerateResource.InvalidFilename", Sources[i].ItemSpec, e.Message);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Remove any output resources that we didn't successfully create (due to error) from the
        /// OutputResources list. Keeps the ordering of OutputResources the same.
        /// </summary>
        /// <remarks>
        /// Q: Why didn't we keep a "successfully created" list instead, like in the Copy task does, which
        /// would save us doing the removal algorithm below?
        /// A: Because we want the ordering of OutputResources to be the same as the ordering passed in.
        /// Some items (the up to date ones) would be added to the successful output list first, and the other items
        /// are added during processing, so the ordering would change. We could fix that up, but it's better to do
        /// the fix up only in the rarer error case. If there were no errors, the algorithm below skips.</remarks>
        private void RemoveUnsuccessfullyCreatedResourcesFromOutputResources()
        {
            // Normally, there aren't any unsuccessful conversions.
            if (_unsuccessfullyCreatedOutFiles == null ||
                _unsuccessfullyCreatedOutFiles.Count == 0)
            {
                return;
            }

            Debug.Assert(OutputResources != null && OutputResources.Length != 0);

            // We only get here if there was at least one resource generation error.
            ITaskItem[] temp = new ITaskItem[OutputResources.Length - _unsuccessfullyCreatedOutFiles.Count];
            int copied = 0;
            int removed = 0;
            foreach (ITaskItem item in OutputResources)
            {
                // Check whether this one is in the bad list.
                if (removed < _unsuccessfullyCreatedOutFiles.Count &&
                    _unsuccessfullyCreatedOutFiles.Contains(item.ItemSpec))
                {
                    removed++;
                }
                else
                {
                    // Copy it to the okay list.
                    temp[copied] = item;
                    copied++;
                }
            }
            OutputResources = temp;
        }

        /// <summary>
        /// Read the state file if able.
        /// </summary>
        private void ReadStateFile()
        {
            // First we look to see if we have a resgen linked files cache.  If so, then we can use that
            // cache to speed up processing.  If there's a problem reading the cache file (or it
            // just doesn't exist, then this method will return a brand new cache object.

            // This method eats IO Exceptions

            cache = ResGenDependencies.DeserializeCache(StateFile?.ItemSpec, UseSourcePath, Log);

            //RWOLFF -- throw here?
            //ErrorUtilities.VerifyThrow(cache != null, "We did not create a cache!");
        }

        /// <summary>
        /// Record the list of file that will be written to disk.
        /// </summary>
        private void RecordFilesWritten()
        {
            // Add any output resources that were successfully created,
            // or would have been if they weren't already up to date (important for Clean)
            foreach (ITaskItem item in OutputResources)
            {
                Debug.Assert(File.Exists(item.ItemSpec), item.ItemSpec + " doesn't exist but we're adding to FilesWritten");
                _filesWritten.Add(new TaskItem(item));
            }

            // Add any state file
            if (StateFile != null && StateFile.ItemSpec.Length > 0)
            {
                // It's possible the file wasn't actually written (eg the path was invalid)
                // We can't easily tell whether that happened here, and I think it's fine to add it anyway.
                _filesWritten.Add(new TaskItem(StateFile));
            }
        }


        /// <summary>
        /// Determines if the given output file is up to date with respect to the
        /// the given input file by comparing timestamps of the two files as well as
        /// (if the source is a .resx) the linked files inside the .resx file itself
        /// <param name="sourceFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <returns></returns>
        private bool ShouldRebuildResgenOutputFile(string sourceFilePath, string outputFilePath)
        {
            bool sourceFileExists = File.Exists(sourceFilePath);
            bool destinationFileExists = File.Exists(outputFilePath);

            // PERF: Regardless of whether the outputFile exists, if the source file is a .resx
            // go ahead and retrieve it from the cache. This is because we want the cache
            // to be populated so that incremental builds can be fast.
            // Note that this is a trade-off: clean builds will be slightly slower. However,
            // for clean builds we're about to read in this very same .resx file so reading
            // it now will page it in. The second read should be cheap.
            ResGenDependencies.ResXFile resxFileInfo = null;
            if (String.Compare(Path.GetExtension(sourceFilePath), ".resx", true, CultureInfo.InvariantCulture) == 0)
            {
                try
                {
                    resxFileInfo = cache.GetResXFileInfo(sourceFilePath);
                }
                catch (ArgumentException)
                {
                    // Return true, so that resource processing will display the error
                    // No point logging a duplicate error here as well
                    return true;
                }
                catch (XmlException)
                {
                    // Return true, so that resource processing will display the error
                    // No point logging a duplicate error here as well
                    return true;
                }
                catch (Exception e)  // Catching Exception, but rethrowing unless it's a well-known exception.
                {
                    ExceptionHandling.RethrowUnlessFileIO(e);
                    // Return true, so that resource processing will display the error
                    // No point logging a duplicate error here as well
                    return true;
                }
            }

            ////////////////////////////////////////////////////////////////////////////////////
            // If the output file does not exist, then we should rebuild it.
            //  Also, if the input file does not exist, we will also return saying that the
            //  the output file needs to be rebuilt, so that this pair of files will
            //  get added to the command-line which will let resgen output whatever error
            //  it normally outputs in the case when users call the tool with bad params
            bool shouldRebuildOutputFile = (!destinationFileExists || !sourceFileExists);

            // if both files do exist, then we need to do some timestamp comparisons
            if (!shouldRebuildOutputFile)
            {
                Debug.Assert(destinationFileExists && sourceFileExists, "GenerateResource task should not check timestamps if neither the .resx nor the .resources files exist");

                // cache the output file timestamps
                DateTime outputFileTimeStamp = File.GetLastWriteTime(outputFilePath);

                // If source file is NOT a .resx, timestamp checking is simple
                if (resxFileInfo == null)
                {
                    // We have a non .resx file. Don't attempt to parse it.

                    // cache the source file timestamp
                    DateTime sourceFileTimeStamp = File.GetLastWriteTime(sourceFilePath);

                    // we need to rebuild this output file if the source file has a
                    //  more recent timestamp than the output file
                    shouldRebuildOutputFile = (sourceFileTimeStamp > outputFileTimeStamp);

                    return shouldRebuildOutputFile;
                }

                // Source file IS a .resx file so we need to do deep dependency analysis
                Debug.Assert(resxFileInfo != null, "Why didn't we get resx file information?");

                // cache the .resx file timestamps
                DateTime resxTimeStamp = resxFileInfo.LastModified;

                // we need to rebuild this .resources file if the .resx file has a
                //  more recent timestamp than the .resources file
                shouldRebuildOutputFile = (resxTimeStamp > outputFileTimeStamp);

                // Check the timestamp of each of the passed-in references against the .RESOURCES file.
                if (!shouldRebuildOutputFile && (References != null))
                {
                    foreach (ITaskItem reference in References)
                    {
                        // If the reference doesn't exist, then we want to rebuild this
                        // .resources file so the user sees an error from ResGen.exe
                        shouldRebuildOutputFile = !File.Exists(reference.ItemSpec);

                        // If the reference exists, then we need to compare the timestamp
                        // for the linked resource to see if it is more recent than the
                        // .resources file
                        if (!shouldRebuildOutputFile)
                        {
                            DateTime referenceTimeStamp = File.GetLastWriteTime(reference.ItemSpec);
                            shouldRebuildOutputFile = referenceTimeStamp > outputFileTimeStamp;
                        }

                        // If we found an instance where a reference is in a state
                        // that we should rebuild the .resources file, then we should
                        // bail from this loop & just return since the first file that
                        // forces a rebuild is enough
                        if (shouldRebuildOutputFile)
                        {
                            break;
                        }
                    }
                }

                // TODO
                //// if the .resources is up to date with respect to the .resx file
                ////  then we need to compare timestamps for each linked file inside
                ////  the .resx file itself
                //if (!shouldRebuildOutputFile && resxFileInfo.LinkedFiles != null)
                //{
                //    foreach (string linkedFilePath in resxFileInfo.LinkedFiles)
                //    {
                //        // If the linked file doesn't exist, then we want to rebuild this
                //        // .resources file so the user sees an error from ResGen.exe
                //        shouldRebuildOutputFile = !File.Exists(linkedFilePath);

                //        // If the linked file exists, then we need to compare the timestamp
                //        // for the linked resource to see if it is more recent than the
                //        // .resources file
                //        if (!shouldRebuildOutputFile)
                //        {
                //            DateTime linkedFileTimeStamp = File.GetLastWriteTime(linkedFilePath);
                //            shouldRebuildOutputFile = linkedFileTimeStamp > outputFileTimeStamp;
                //        }

                //        // If we found an instance where a linked file is in a state
                //        // that we should rebuild the .resources file, then we should
                //        // bail from this loop & just return since the first file that
                //        // forces a rebuild is enough
                //        if (shouldRebuildOutputFile)
                //        {
                //            break;
                //        }
                //    }
                //}
            }

            return shouldRebuildOutputFile;
            //#endif
        }

        /// <summary>
        /// Create the AssemblyName array that ProcessResources will need.
        /// </summary>
        /// <returns>AssemblyName array</returns>
        /// <owner>danmose</owner>
        /// <throws>ArgumentException</throws>
        private AssemblyName[] LoadReferences()
        {
            if (References == null)
            {
                return new AssemblyName[0];
            }

            AssemblyName[] assemblyList = new AssemblyName[References.Length];

            for (int i = 0; i < References.Length; i++)
            {
                try
                {
                    assemblyList[i] = AssemblyName.GetAssemblyName(References[i].ItemSpec);
                }
                // We should never get passed in references we can't load. In the VS build process, for example,
                // we're passed in @(ReferencePath), which only contains resolved references.
                catch (ArgumentNullException e)
                {
                    throw new ArgumentException(e.Message, References[i].ItemSpec);
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException(e.Message, References[i].ItemSpec);
                }
                catch (FileNotFoundException e)
                {
                    throw new ArgumentException(e.Message, References[i].ItemSpec);
                }
                /*catch (SecurityException e)
                {
                    throw new ArgumentException(e.Message, References[i].ItemSpec);
                }
                */
                catch (BadImageFormatException e)
                {
                    throw new ArgumentException(e.Message, References[i].ItemSpec);
                }
                catch (FileLoadException e)
                {
                    throw new ArgumentException(e.Message, References[i].ItemSpec);
                }
            }

            return assemblyList;
        }

        /// <summary>
        /// Write the state file if there is one to be written.
        /// </summary>
        private void WriteStateFile()
        {
            if (cache.IsDirty)
            {
                // And now we serialize the cache to save our resgen linked file resolution for later use.
                cache.SerializeCache(StateFile?.ItemSpec, Log);
            }
        }
    }
}
