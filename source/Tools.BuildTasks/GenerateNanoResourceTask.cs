//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace nanoFramework.Tools
{
    [Description("GenerateNanoResourceTaskEntry")]
    public class GenerateNanoResourceTask : Task
    {
        // This cache helps us track the linked resource files listed inside of a resx resource file
        // TODO private ResGenDependencies cache;

        /// <summary>
        /// List of output files that we failed to create due to an error.
        /// See note in RemoveUnsuccessfullyCreatedResourcesFromOutputResources()
        /// </summary>
        private List<string> UnsuccessfullyCreatedOutFiles = new List<string>();


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
        public ITaskItem[] FilesWritten { get { return _FilesWritten.ToArray(); } private set { } }
        private List<ITaskItem> _FilesWritten = new List<ITaskItem>();

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
                    //Sources[i].CopyMetadataTo(OutputResources[i]);

                    if (!File.Exists(Sources[i].ItemSpec))
                    {
                        // Error but continue with the files that do exist
                        Log.LogError("GenerateResource.ResourceNotFound", Sources[i].ItemSpec);
                        UnsuccessfullyCreatedOutFiles.Add(OutputResources[i].ItemSpec);
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

                    AppDomain appDomain = null;
                    // TODO ProcessResourceFiles process = null;

                    try
                    {
                        // TODO process = new ProcessResourceFiles();

                        //setup strongly typed class name??

                        // TODO
                        //process.Run(Log, assemblyList, (ITaskItem[])inputsToProcess.ToArray(), (ITaskItem[])outputsToProcess.ToArray(),
                        //    UseSourcePath);

                        //if (null != process.UnsuccessfullyCreatedOutFiles)
                        //{
                        //    foreach (string item in process.UnsuccessfullyCreatedOutFiles)
                        //    {
                        //        UnsuccessfullyCreatedOutFiles.Add(item);
                        //    }
                        //}
                        //process = null;
                    }
                    finally
                    {
                    }
                }

                // And now we serialize the cache to save our resgen linked file resolution for later use.
                // TODO WriteStateFile();

                // TODO RemoveUnsuccessfullyCreatedResourcesFromOutputResources();

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
        /// <owner>danmose</owner>
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
        /// Read the state file if able.
        /// </summary>
        private void ReadStateFile()
        {
            // First we look to see if we have a resgen linked files cache.  If so, then we can use that
            // cache to speed up processing.  If there's a problem reading the cache file (or it
            // just doesn't exist, then this method will return a brand new cache object.

            // This method eats IO Exceptions

            // TODO cache = ResGenDependencies.DeserializeCache((StateFile == null) ? null : StateFile.ItemSpec, UseSourcePath, Log);

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
            foreach (ITaskItem item in this.OutputResources)
            {
                Debug.Assert(File.Exists(item.ItemSpec), item.ItemSpec + " doesn't exist but we're adding to FilesWritten");
                _FilesWritten.Add(new TaskItem(item));
            }

            // Add any state file
            if (StateFile != null && StateFile.ItemSpec.Length > 0)
            {
                // It's possible the file wasn't actually written (eg the path was invalid)
                // We can't easily tell whether that happened here, and I think it's fine to add it anyway.
                _FilesWritten.Add(new TaskItem(StateFile));
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
            /*#if !(MSBUILD_SOURCES)
                        return true;
            #else
             */
            bool sourceFileExists = File.Exists(sourceFilePath);
            bool destinationFileExists = File.Exists(outputFilePath);

            // PERF: Regardless of whether the outputFile exists, if the source file is a .resx
            // go ahead and retrieve it from the cache. This is because we want the cache
            // to be populated so that incremental builds can be fast.
            // Note that this is a trade-off: clean builds will be slightly slower. However,
            // for clean builds we're about to read in this very same .resx file so reading
            // it now will page it in. The second read should be cheap.
            // TODO ResGenDependencies.ResXFile resxFileInfo = null;
            //if (String.Compare(Path.GetExtension(sourceFilePath), ".resx", true, CultureInfo.InvariantCulture) == 0)
            //{
            //    try
            //    {
            //        resxFileInfo = cache.GetResXFileInfo(sourceFilePath);
            //    }
            //    catch (ArgumentException)
            //    {
            //        // Return true, so that resource processing will display the error
            //        // No point logging a duplicate error here as well
            //        return true;
            //    }
            //    catch (XmlException)
            //    {
            //        // Return true, so that resource processing will display the error
            //        // No point logging a duplicate error here as well
            //        return true;
            //    }
            //    catch (Exception e)  // Catching Exception, but rethrowing unless it's a well-known exception.
            //    {
            //        ExceptionHandling.RethrowUnlessFileIO(e);
            //        // Return true, so that resource processing will display the error
            //        // No point logging a duplicate error here as well
            //        return true;
            //    }
            //}

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

                // TODO
                //// If source file is NOT a .resx, timestamp checking is simple
                //if (resxFileInfo == null)
                //{
                //    // We have a non .resx file. Don't attempt to parse it.

                //    // cache the source file timestamp
                //    DateTime sourceFileTimeStamp = File.GetLastWriteTime(sourceFilePath);

                //    // we need to rebuild this output file if the source file has a
                //    //  more recent timestamp than the output file
                //    shouldRebuildOutputFile = (sourceFileTimeStamp > outputFileTimeStamp);

                //    return shouldRebuildOutputFile;
                //}

                //// Source file IS a .resx file so we need to do deep dependency analysis
                //Debug.Assert(resxFileInfo != null, "Why didn't we get resx file information?");

                //// cache the .resx file timestamps
                //DateTime resxTimeStamp = resxFileInfo.LastModified;

                //// we need to rebuild this .resources file if the .resx file has a
                ////  more recent timestamp than the .resources file
                //shouldRebuildOutputFile = (resxTimeStamp > outputFileTimeStamp);

                // Check the timestamp of each of the passed-in references against the .RESOURCES file.
                if (!shouldRebuildOutputFile && (this.References != null))
                {
                    foreach (ITaskItem reference in this.References)
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
                return null;
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
            // TODO 
            //if (cache.IsDirty)
            //{
            //    // And now we serialize the cache to save our resgen linked file resolution for later use.
            //    cache.SerializeCache((StateFile == null) ? null : StateFile.ItemSpec, Log);
            //}
        }
    }
}
