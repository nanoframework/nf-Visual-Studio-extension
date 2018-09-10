//
// Copyright (c) 2018 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.Designer.Interfaces;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal abstract class BaseCodeGenerator : IVsSingleFileGenerator
    {
        private string codeFileNameSpace = string.Empty;
        private string codeFilePath = string.Empty;

        private IVsGeneratorProgress codeGeneratorProgress;

        // **************************** PROPERTIES ****************************
        protected string FileNameSpace
        {
            get
            {
                return codeFileNameSpace;
            }
        }

        protected string InputFilePath
        {
            get
            {
                return codeFilePath;
            }
        }

        internal IVsGeneratorProgress CodeGeneratorProgress
        {
            get
            {
                return codeGeneratorProgress;
            }
        }

        // **************************** METHODS **************************
        public abstract int DefaultExtension(out string ext);

        // MUST implement this abstract method.
        protected abstract byte[] GenerateCode(string inputFileName, string inputFileContent);


        protected virtual void GeneratorErrorCallback(int warning, uint level, string message, uint line, uint column)
        {
            IVsGeneratorProgress progress = CodeGeneratorProgress;
            if (progress != null)
            {
                //Utility.ThrowOnFailure(progress.GeneratorError(warning, level, message, line, column));

            }
        }

        public int Generate(string wszInputFilePath, string bstrInputFileContents, string wszDefaultNamespace,
                             IntPtr[] pbstrOutputFileContents, out uint pbstrOutputFileContentSize, IVsGeneratorProgress pGenerateProgress)
        {

            if (bstrInputFileContents == null)
            {
                throw new ArgumentNullException(bstrInputFileContents);
            }
            codeFilePath = wszInputFilePath;
            codeFileNameSpace = wszDefaultNamespace;
            codeGeneratorProgress = pGenerateProgress;

            byte[] bytes = GenerateCode(wszInputFilePath, bstrInputFileContents);
            if (bytes == null)
            {
                pbstrOutputFileContents[0] = IntPtr.Zero;
                pbstrOutputFileContentSize = 0;
            }
            else
            {
                pbstrOutputFileContents[0] = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, pbstrOutputFileContents[0], bytes.Length);
                pbstrOutputFileContentSize = (uint)bytes.Length;
            }
            return COM_HResults.S_OK;
        }

        protected byte[] StreamToBytes(Stream stream)
        {
            if (stream.Length == 0)
                return new byte[] { };

            long position = stream.Position;
            stream.Position = 0;
            byte[] bytes = new byte[(int)stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            stream.Position = position;

            return bytes;
        }
    }

    internal abstract class BaseCodeGeneratorWithSite : BaseCodeGenerator, IObjectWithSite
    {

        private Object site = null;
        private CodeDomProvider codeDomProvider = null;
        private static Guid CodeDomInterfaceGuid = new Guid("{73E59688-C7C4-4a85-AF64-A538754784C5}");
        private static Guid CodeDomServiceGuid = CodeDomInterfaceGuid;
        private ServiceProvider serviceProvider = null;

        protected virtual CodeDomProvider CodeProvider
        {
            get
            {
                if (codeDomProvider == null)
                {
                    IVSMDCodeDomProvider vsmdCodeDomProvider = (IVSMDCodeDomProvider)GetService(CodeDomServiceGuid);
                    if (vsmdCodeDomProvider != null)
                    {
                        codeDomProvider = (CodeDomProvider)vsmdCodeDomProvider.CodeDomProvider;
                    }
                    Debug.Assert(codeDomProvider != null, "Get CodeDomProvider Interface failed.  GetService(QueryService(CodeDomProvider) returned Null.");
                }
                return codeDomProvider;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException();
                }

                codeDomProvider = value;
            }
        }

        private ServiceProvider SiteServiceProvider
        {
            get
            {
                if (serviceProvider == null)
                {
                    IOleServiceProvider oleServiceProvider = site as IOleServiceProvider;
                    Debug.Assert(oleServiceProvider != null, "Unable to get IOleServiceProvider from site object.");

                    serviceProvider = new ServiceProvider(oleServiceProvider);
                }
                return serviceProvider;
            }
        }

        protected Object GetService(Guid serviceGuid)
        {
            return SiteServiceProvider.GetService(serviceGuid);
        }

        protected object GetService(Type serviceType)
        {
            return SiteServiceProvider.GetService(serviceType);
        }


        public override int DefaultExtension(out string ext)
        {
            CodeDomProvider codeDom = CodeProvider;
            Debug.Assert(codeDom != null, "CodeDomProvider is NULL.");
            string extension = codeDom.FileExtension;
            if (extension != null && extension.Length > 0)
            {
                if (extension[0] != '.')
                {
                    extension = "." + extension;
                }
            }

            ext = extension;
            return COM_HResults.S_OK;
        }

        protected virtual ICodeGenerator GetCodeWriter()
        {
            CodeDomProvider codeDom = CodeProvider;
            if (codeDom != null)
            {
#pragma warning disable 618 //backwards compat
                return codeDom.CreateGenerator();
#pragma warning restore 618
            }

            return null;
        }

        // ******************* Implement IObjectWithSite *****************
        //
        public virtual void SetSite(object pUnkSite)
        {
            site = pUnkSite;
            codeDomProvider = null;
            serviceProvider = null;
        }

        // Does anyone rely on this method?
        public virtual void GetSite(ref Guid riid, out IntPtr ppvSite)
        {
            if (site == null)
            {
                //COM_HResults.Throw(Utility.COM_HResults.E_FAIL);
            }

            IntPtr pUnknownPointer = Marshal.GetIUnknownForObject(site);
            try
            {
                Marshal.QueryInterface(pUnknownPointer, ref riid, out ppvSite);

                if (ppvSite == IntPtr.Zero)
                {
                    //Utility.COM_HResults.Throw(Utility.COM_HResults.E_NOINTERFACE);
                }
            }
            finally
            {
                if (pUnknownPointer != IntPtr.Zero)
                {
                    Marshal.Release(pUnknownPointer);
                    pUnknownPointer = IntPtr.Zero;
                }
            }
        }
    }

    [ComVisible(true)]
    [CodeGeneratorRegistration(typeof(ResXFileCodeGenerator), "nanoFramework resx code-behind generator", NanoFrameworkPackage.ProjectTypeGuid, GeneratesDesignTimeSource = true)]
    [ProvideObject(typeof(ResXFileCodeGenerator))]

    internal class ResXFileCodeGenerator : BaseCodeGeneratorWithSite, IObjectWithSite
    {
        internal const string c_Name = "ResXFileCodeGenerator";
        private const string DesignerExtension = ".Designer";

        internal bool m_fInternal = true;
        internal bool m_fNestedEnums = true;
        internal bool m_fMscorlib = false;

        protected string GetResourcesNamespace()
        {
            string resourcesNamespace = null;
            try
            {
                IntPtr punkVsBrowseObject;
                Guid vsBrowseObjectGuid = typeof(IVsBrowseObject).GUID;

                GetSite(ref vsBrowseObjectGuid, out punkVsBrowseObject);

                if (punkVsBrowseObject != IntPtr.Zero)
                {

                    IVsBrowseObject vsBrowseObject = Marshal.GetObjectForIUnknown(punkVsBrowseObject) as IVsBrowseObject;
                    Debug.Assert(vsBrowseObject != null, "Generator invoked by Site that is not IVsBrowseObject?");

                    Marshal.Release(punkVsBrowseObject);

                    if (vsBrowseObject != null)
                    {
                        IVsHierarchy vsHierarchy;
                        uint vsitemid;

                        vsBrowseObject.GetProjectItem(out vsHierarchy, out vsitemid);

                        Debug.Assert(vsHierarchy != null, "GetProjectItem should have thrown or returned a valid IVsHierarchy");
                        Debug.Assert(vsitemid != 0, "GetProjectItem should have thrown or returned a valid VSITEMID");


                        if (vsHierarchy != null)
                        {
                            object obj;

                            vsHierarchy.GetProperty(vsitemid, (int)__VSHPROPID.VSHPROPID_DefaultNamespace, out obj);
                            string objStr = obj as string;
                            if (objStr != null)
                            {
                                resourcesNamespace = objStr;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
                Debug.Fail("These methods should succeed...");
            }

            return resourcesNamespace;
        }

        public override int DefaultExtension(out string ext)
        {
            //copied from ResXFileCodeGenerator
            string baseExtension;
            ext = String.Empty;

            int hResult = base.DefaultExtension(out baseExtension);

            if(hResult != COM_HResults.S_OK)
            {
                Debug.Fail("Invalid hresult returned by the base DefaultExtension");
                return hResult;
            }

            if (!String.IsNullOrEmpty(baseExtension))
            {
                ext = DesignerExtension + baseExtension;
            }

            return COM_HResults.S_OK;
        }

        protected override byte[] GenerateCode(string inputFileName, string inputFileContent)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            string inputFileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFileName);

            Assembly tasks = GetType().Assembly;

            Type typ = tasks.GetType("nanoFramework.Tools.VisualStudio.Extension.ProcessResourceFiles");

            if (typ != null)
            {
                object processResourceFiles = typ.GetConstructor(new Type[] { }).Invoke(null);

                typ.GetProperty("StronglyTypedClassName").SetValue(processResourceFiles, inputFileNameWithoutExtension, null);
                typ.GetProperty("StronglyTypedNamespace").SetValue(processResourceFiles, GetResourcesNamespace(), null);
                typ.GetProperty("GenerateNestedEnums").SetValue(processResourceFiles, m_fNestedEnums, null);
                typ.GetProperty("GenerateInternalClass").SetValue(processResourceFiles, m_fInternal, null);
                typ.GetProperty("IsMscorlib").SetValue(processResourceFiles, m_fMscorlib, null);

                string resourceName = (string)typ.GetProperty("StronglyTypedNamespace").GetValue(processResourceFiles, null);

                if (string.IsNullOrEmpty(resourceName))
                {
                    resourceName = inputFileNameWithoutExtension;
                }
                else
                {
                    resourceName = string.Format("{0}.{1}", resourceName, inputFileNameWithoutExtension);
                }

                typ.GetMethod("CreateStronglyTypedResources").Invoke(processResourceFiles, new object[] { inputFileName, this.CodeProvider, writer, resourceName });
            }

            return base.StreamToBytes(stream);
        }

        internal abstract class BaseCodeGenerator : IVsSingleFileGenerator
        {
            private string codeFileNameSpace = string.Empty;
            private string codeFilePath = string.Empty;

            private IVsGeneratorProgress codeGeneratorProgress;

            // **************************** PROPERTIES ****************************
            protected string FileNameSpace
            {
                get
                {
                    return codeFileNameSpace;
                }
            }

            protected string InputFilePath
            {
                get
                {
                    return codeFilePath;
                }
            }

            internal IVsGeneratorProgress CodeGeneratorProgress
            {
                get
                {
                    return codeGeneratorProgress;
                }
            }

            // **************************** METHODS **************************
            public abstract int DefaultExtension(out string ext);

            // MUST implement this abstract method.
            protected abstract byte[] GenerateCode(string inputFileName, string inputFileContent);


            protected virtual void GeneratorErrorCallback(int warning, uint level, string message, uint line, uint column)
            {
                IVsGeneratorProgress progress = CodeGeneratorProgress;
                if (progress != null)
                {
                    progress.GeneratorError(warning, level, message, line, column);
                }
            }

            public int Generate(string wszInputFilePath, string bstrInputFileContents, string wszDefaultNamespace,
                                 IntPtr[] pbstrOutputFileContents, out uint pbstrOutputFileContentSize, IVsGeneratorProgress pGenerateProgress)
            {

                if (bstrInputFileContents == null)
                {
                    throw new ArgumentNullException(bstrInputFileContents);
                }
                codeFilePath = wszInputFilePath;
                codeFileNameSpace = wszDefaultNamespace;
                codeGeneratorProgress = pGenerateProgress;

                byte[] bytes = GenerateCode(wszInputFilePath, bstrInputFileContents);
                if (bytes == null)
                {
                    pbstrOutputFileContents[0] = IntPtr.Zero;
                    pbstrOutputFileContentSize = 0;
                }
                else
                {
                    pbstrOutputFileContents[0] = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, pbstrOutputFileContents[0], bytes.Length);
                    pbstrOutputFileContentSize = (uint)bytes.Length;
                }
                return COM_HResults.S_OK;
            }

            protected byte[] StreamToBytes(Stream stream)
            {
                if (stream.Length == 0)
                    return new byte[] { };

                long position = stream.Position;
                stream.Position = 0;
                byte[] bytes = new byte[(int)stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                stream.Position = position;

                return bytes;
            }
        }

        internal abstract class BaseCodeGeneratorWithSite : BaseCodeGenerator, IObjectWithSite
        {
            private Object site = null;
            private CodeDomProvider codeDomProvider = null;
            private static Guid CodeDomInterfaceGuid = new Guid("{73E59688-C7C4-4a85-AF64-A538754784C5}");
            private static Guid CodeDomServiceGuid = CodeDomInterfaceGuid;
            private ServiceProvider serviceProvider = null;

            protected virtual CodeDomProvider CodeProvider
            {
                get
                {
                    if (codeDomProvider == null)
                    {
                        IVSMDCodeDomProvider vsmdCodeDomProvider = (IVSMDCodeDomProvider)GetService(CodeDomServiceGuid);
                        if (vsmdCodeDomProvider != null)
                        {
                            codeDomProvider = (CodeDomProvider)vsmdCodeDomProvider.CodeDomProvider;
                        }
                        Debug.Assert(codeDomProvider != null, "Get CodeDomProvider Interface failed.  GetService(QueryService(CodeDomProvider) returned Null.");
                    }
                    return codeDomProvider;
                }
                set
                {
                    if (value == null)
                    {
                        throw new ArgumentNullException();
                    }

                    codeDomProvider = value;
                }
            }

            private ServiceProvider SiteServiceProvider
            {
                get
                {
                    if (serviceProvider == null)
                    {
                        IOleServiceProvider oleServiceProvider = site as IOleServiceProvider;
                        Debug.Assert(oleServiceProvider != null, "Unable to get IOleServiceProvider from site object.");

                        serviceProvider = new ServiceProvider(oleServiceProvider);
                    }
                    return serviceProvider;
                }
            }

            protected Object GetService(Guid serviceGuid)
            {
                return SiteServiceProvider.GetService(serviceGuid);
            }

            protected object GetService(Type serviceType)
            {
                return SiteServiceProvider.GetService(serviceType);
            }


            public override int DefaultExtension(out string ext)
            {
                CodeDomProvider codeDom = CodeProvider;
                Debug.Assert(codeDom != null, "CodeDomProvider is NULL.");
                string extension = codeDom.FileExtension;
                if (extension != null && extension.Length > 0)
                {
                    if (extension[0] != '.')
                    {
                        extension = "." + extension;
                    }
                }

                ext = extension;
                return COM_HResults.S_OK;
            }

            protected virtual ICodeGenerator GetCodeWriter()
            {
                CodeDomProvider codeDom = CodeProvider;
                if (codeDom != null)
                {
#pragma warning disable 618 //backwards compat
                    return codeDom.CreateGenerator();
#pragma warning restore 618
                }

                return null;
            }

            // ******************* Implement IObjectWithSite *****************
            //
            public virtual void SetSite(object pUnkSite)
            {
                site = pUnkSite;
                codeDomProvider = null;
                serviceProvider = null;
            }

            // Does anyone rely on this method?
            public virtual void GetSite(ref Guid riid, out IntPtr ppvSite)
            {
                if (site == null)
                {
                   // COM_HResults.Throw(COM_HResults.E_FAIL);
                }

                IntPtr pUnknownPointer = Marshal.GetIUnknownForObject(site);
                try
                {
                    Marshal.QueryInterface(pUnknownPointer, ref riid, out ppvSite);

                    if (ppvSite == IntPtr.Zero)
                    {
                        //COM_HResults.Throw(COM_HResults.E_NOINTERFACE);
                    }
                }
                finally
                {
                    if (pUnknownPointer != IntPtr.Zero)
                    {
                        Marshal.Release(pUnknownPointer);
                        pUnknownPointer = IntPtr.Zero;
                    }
                }
            }
        }
    }


    /// <summary>
    /// This class handles the processing of source resource files into compiled resource files.
    /// Its designed to be called from a separate AppDomain so that any files locked by ResXResourceReader
    /// can be released.
    /// </summary>
    public sealed class ProcessResourceFiles : MarshalByRefObject
    {

        #region fields
        /// <summary>
        /// Resource list (used to preserve resource ordering, primarily for easier testing)
        /// </summary>
        private ArrayList resources = new ArrayList();

        /// <summary>
        /// Mirror resource list, used to check for duplicates
        /// </summary>
        private Hashtable resourcesHashTable = new Hashtable(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// Logger for any messages or errors
        /// </summary>
        private TaskLoggingHelper logger = null;

        /// <summary>
        /// Language for the strongly typed resources.
        /// </summary>
        //private string stronglyTypedLanguage;

        /// <summary>
        /// Filename for the strongly typed resources.
        /// Getter provided since the processor may choose a default.
        /// </summary>

        /// <summary>
        /// Namespace for the strongly typed resources.
        /// </summary>
        private string stronglyTypedNamespace;

        public string StronglyTypedNamespace
        {
            get { return this.stronglyTypedNamespace; }
            set { this.stronglyTypedNamespace = value; }
        }

        /// <summary>
        /// Class name for the strongly typed resources.
        /// Getter provided since the processor may choose a default.
        /// </summary>
        public string StronglyTypedClassName
        {
            get
            {
                return this.stronglyTypedClassName;
            }
            set
            {
                this.stronglyTypedClassName = value;
            }
        }
        private string stronglyTypedClassName;

        private bool useNestedClassForEnums = true;
        private bool useInternalClass = true;

        /// <summary>
        /// List of assemblies to use for type resolution within resx files
        /// </summary>
        private AssemblyName[] assemblyList;

        /// <summary>
        /// List of input files to process.
        /// </summary>
        private ITaskItem[] inFiles;

        /// <summary>
        /// List of output files to process.
        /// </summary>
        private ITaskItem[] outFiles;

        public bool GenerateNestedEnums
        {
            get { return this.useNestedClassForEnums; }
            set { this.useNestedClassForEnums = value; }
        }

        public bool GenerateInternalClass
        {
            get { return this.useInternalClass; }
            set { this.useInternalClass = value; }
        }

        public bool IsMscorlib
        {
            get { return isMscorlib; }
            set { this.isMscorlib = value; }
        }


        /// <summary>
        /// List of output files that we failed to create due to an error.
        /// See note in RemoveUnsuccessfullyCreatedResourcesFromOutputResources()
        /// </summary>
        internal ArrayList UnsuccessfullyCreatedOutFiles
        {
            get
            {
                if (null == unsuccessfullyCreatedOutFiles)
                {
                    unsuccessfullyCreatedOutFiles = new ArrayList();
                }
                return unsuccessfullyCreatedOutFiles;
            }
        }
        private ArrayList unsuccessfullyCreatedOutFiles;

        /// <summary>
        /// Whether we successfully created the STR class
        /// </summary>
        internal bool StronglyTypedResourceSuccessfullyCreated
        {
            get
            {
                return stronglyTypedResourceSuccessfullyCreated;
            }
        }
        private bool stronglyTypedResourceSuccessfullyCreated = false;

        private bool isMscorlib = false;

        /// <summary>
        /// Indicates whether the resource reader should use the source file's
        /// directory to resolve relative file paths.
        /// </summary>
        private bool useSourcePath = false;

        private ITaskItem m_inTaskItem;
        private ITaskItem m_outTaskItem;

        #endregion

        /// <summary>
        /// Process all files.
        /// </summary>
        internal void Run(TaskLoggingHelper log, AssemblyName[] assemblies, ITaskItem[] inputs, ITaskItem[] outputs, bool sourcePath)
        {
            logger = log;
            assemblyList = assemblies;
            inFiles = inputs;
            outFiles = outputs;
            useSourcePath = sourcePath;

            for (int i = 0; i < inFiles.Length; ++i)
            {
                if (!ProcessFile(inFiles[i], outFiles[i]))
                {
                    UnsuccessfullyCreatedOutFiles.Add(outFiles[i]);
                }
            }
        }

        private void Init()
        {
            this.resources.Clear();
            this.resourcesHashTable.Clear();
        }

        private void InitFileProcessing(ITaskItem inTaskItem, ITaskItem outTaskItem)
        {
            Init();

            m_inTaskItem = inTaskItem;
            m_outTaskItem = outTaskItem;
        }

        #region Code from ResGen.EXE
        /// <summary>
        /// Read all resources from a file and write to a new file in the chosen format
        /// </summary>
        /// <remarks>Uses the input and output file extensions to determine their format</remarks>
        /// <param name="inFile">Input resources file</param>
        /// <param name="outFile">Output resources file</param>
        /// <returns>True if conversion was successful, otherwise false</returns>
        private bool ProcessFile(ITaskItem inTaskItem, ITaskItem outTaskItem)
        {
            InitFileProcessing(inTaskItem, outTaskItem);

            string inFile = inTaskItem.ItemSpec;
            string outFile = outTaskItem.ItemSpec;

            if (GetFormat(inFile) == Format.Error)
            {
                logger.LogError("GenerateResource.UnknownFileExtension", Path.GetExtension(inFile), inFile);
                return false;
            }
            if (GetFormat(outFile) == Format.Error)
            {
                logger.LogError("GenerateResource.UnknownFileExtension", Path.GetExtension(outFile), outFile);
                return false;
            }

            //            logger.LogMessage("GenerateResource.ProcessingFile", inFile, outFile);

            try
            {
                ReadResources(inFile, useSourcePath);
            }
            catch (ArgumentException ae)
            {
                if (ae.InnerException is XmlException)
                {
                    XmlException xe = (XmlException)ae.InnerException;
                    logger.LogError(null, inFile, xe.LineNumber, xe.LinePosition, 0, 0, "General.InvalidResxFile", xe.Message);
                }
                else
                {
                    logger.LogError(null, inFile, 0, 0, 0, 0, "General.InvalidResxFile", ae.Message);
                }
                return false;
            }
            catch (XmlException xe)
            {
                logger.LogError(null, inFile, xe.LineNumber, xe.LinePosition, 0, 0, "General.InvalidResxFile", xe.Message);
                return false;
            }
#if MSBUILD_SOURCES
            catch (Exception e)
            {
                ExceptionHandling.RethrowUnlessFileIO(e);
                logger.LogError(null, inFile, 0, 0, 0, 0, "General.InvalidResxFile", e.Message);

                // We need to give meaningful error messages to the user.
                // Note that ResXResourceReader wraps any exception it gets
                // in an ArgumentException with the message "Invalid ResX input."
                // If you don't look at the InnerException, you have to attach
                // a debugger to find the problem.
                if (e.InnerException != null)
                {
                    Exception inner = e.InnerException;
                    StringBuilder sb = new StringBuilder(200);
                    sb.Append(e.Message);
                    while (inner != null)
                    {
                        sb.Append(" ---> ");
                        sb.Append(inner.GetType().Name);
                        sb.Append(": ");
                        sb.Append(inner.Message);
                        inner = inner.InnerException;
                    }
                    logger.LogError(null, inFile, 0, 0, 0, 0, "General.InvalidResxFile", sb.ToString());
                }
                return false;
            }
#endif
            catch
            {
                throw;
            }

            try
            {
                WriteResources(outFile);
            }
            catch (IOException io)
            {
                logger.LogError("GenerateResource.CannotWriteOutput", outFile, io.Message);

                if (File.Exists(outFile))
                {
                    logger.LogError("GenerateResource.CorruptOutput", outFile);
                    try
                    {
                        File.Delete(outFile);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning("GenerateResource.DeleteCorruptOutputFailed", outFile, e.Message);
                    }
                }
                return false;
            }
#if MSBUILD_SOURCES
            catch (Exception e)
            {
                ExceptionHandling.RethrowUnlessFileIO(e);
                logger.LogError("GenerateResource.CannotWriteOutput", outFile, e.Message);
                return false;
            }
#endif
            catch
            {
                throw;
            }

            return true;
        }

        /// <summary>
        /// Figure out the format of an input resources file from the extension
        /// </summary>
        /// <param name="filename">Input resources file</param>
        /// <returns>Resources format</returns>
        private Format GetFormat(string filename)
        {
            string extension = Path.GetExtension(filename);
            if (String.Compare(extension, ".txt", true, CultureInfo.InvariantCulture) == 0 ||
                String.Compare(extension, ".restext", true, CultureInfo.InvariantCulture) == 0)
            {
                return Format.Text;
            }
            else if (String.Compare(extension, ".resx", true, CultureInfo.InvariantCulture) == 0)
            {
                return Format.XML;
            }
            else if (String.Compare(extension, ".resources", true, CultureInfo.InvariantCulture) == 0)
            {
                return Format.Binary;
            }
            else if (String.Compare(extension, ".tinyresources", true, CultureInfo.InvariantCulture) == 0)
            {
                return Format.TinyResources;
            }
            else
            {
                return Format.Error;
            }
        }

        /// <summary>
        /// Text files are just name/value pairs.  ResText is the same format
        /// with a unique extension to work around some ambiguities with MSBuild
        /// ResX is our existing XML format from V1.
        /// </summary>
        private enum Format
        {
            Text, // .txt or .restext
            XML, // .resx
            Binary, // .resources
            TinyResources, //.tinyresources
            Error, // anything else
        }

        /// <summary>
        /// Reads the resources out of the specified file and populates the
        /// resources hashtable.
        /// </summary>
        /// <param name="filename">Filename to load</param>
        /// <param name="shouldUseSourcePath">Whether to resolve paths in the
        /// resources file relative to the resources file location</param>
        public void ReadResources(String filename, bool shouldUseSourcePath)
        {
            // Reset state
            //            resources.Clear();
            //            resourcesHashTable.Clear();

            Format format = GetFormat(filename);
            switch (format)
            {
                case Format.Text:
                    ReadTextResources(filename);
                    break;

                case Format.XML:
                    ResXResourceReader resXReader = null;
                    if (assemblyList != null)
                    {
                        resXReader = new ResXResourceReader(filename, assemblyList);
                    }
                    else
                    {
                        resXReader = new ResXResourceReader(filename);
                    }
                    if (shouldUseSourcePath)
                    {
                        String fullPath = Path.GetFullPath(filename);
                        resXReader.BasePath = Path.GetDirectoryName(fullPath);
                    }
                    // ReadResources closes the reader for us
                    ReadResources(resXReader, filename);
                    break;

                case Format.Binary:
                    ReadResources(new ResourceReader(filename), filename); // closes reader for us
                    break;
                case Format.TinyResources:
                    Debug.Fail("Unknown format " + format.ToString());
                    break;

                default:
                    // We should never get here, we've already checked the format
                    Debug.Fail("Unknown format " + format.ToString());
                    return;
            }
            //logger.LogMessage(BuildEventImportance.Low/*MessageImportance.Low*/, "GenerateResource.ReadResourceMessage", resources.Count, filename);
        }

        /// <summary>
        /// Write resources from the resources ArrayList to the specified output file
        /// </summary>
        /// <param name="filename">Output resources file</param>
        public void WriteResources(String filename)
        {
            Format format = GetFormat(filename);
            switch (format)
            {
                case Format.Text:
                    WriteTextResources(filename);
                    break;

                case Format.XML:
                    WriteResources(new ResXResourceWriter(filename)); // closes writer for us
                    break;

                case Format.Binary:
                    WriteResources(new ResourceWriter(filename)); // closes writer for us
                    break;

                case Format.TinyResources:
                    WriteResources(new TinyResourceWriter(filename)); // closes writer for us
                    break;
                default:
                    // We should never get here, we've already checked the format
                    Debug.Fail("Unknown format " + format.ToString());
                    break;
            }
        }

        private void CreateStronglyTypedResources(CodeDomProvider provider, TextWriter writer, string resourceName, out string[] errors)
        {
            CodeCompileUnit ccu = CreateStronglyTypedResourceFile(resourceName, resources,
                stronglyTypedClassName, stronglyTypedNamespace, provider, out errors);
            CodeGeneratorOptions codeGenOptions = new CodeGeneratorOptions();
            codeGenOptions.BlankLinesBetweenMembers = false;
            codeGenOptions.BracingStyle = "C";

            provider.GenerateCodeFromCompileUnit(ccu, writer, codeGenOptions);
            writer.Flush();
        }

        public void CreateStronglyTypedResources(string inputFileName, CodeDomProvider provider, TextWriter writer, string resourceName)
        {
            Init();

            ReadResources(inputFileName, true);

            string[] errors = null;

            CreateStronglyTypedResources(provider, writer, resourceName, out errors);

            if (errors != null && errors.Length > 0)
            {
                throw new ApplicationException(errors[0]);
            }
        }

        private CodeNamespace CreateNamespace(CodeCompileUnit ccu, string ns, Hashtable tableNamespaces)
        {
            CodeNamespace codeNamespace = (CodeNamespace)tableNamespaces[ns];

            if (codeNamespace == null)
            {
                codeNamespace = new CodeNamespace(ns);
                ccu.Namespaces.Add(codeNamespace);
                tableNamespaces[ns] = codeNamespace;
            }

            return codeNamespace;
        }

        private CodeTypeDeclaration CreateTypeDeclaration(CodeNamespace codeNamespace, string type, Hashtable tableTypes)
        {
            CodeTypeDeclaration codeTypeDeclaration = (CodeTypeDeclaration)tableTypes[type];

            if (codeTypeDeclaration == null)
            {
                int iPlus = type.LastIndexOf('+');

                if (iPlus < 0)
                {
                    codeTypeDeclaration = new CodeTypeDeclaration(type);
                    codeTypeDeclaration.IsPartial = true;

                    codeNamespace.Types.Add(codeTypeDeclaration);
                }
                else
                {
                    string typeBase = type.Substring(0, iPlus);
                    string typeNested = type.Substring(iPlus + 1);

                    CodeTypeDeclaration codeTypeDeclarationBase = CreateTypeDeclaration(codeNamespace, typeBase, tableTypes);
                    codeTypeDeclaration = new CodeTypeDeclaration(typeNested);

                    codeTypeDeclarationBase.Members.Add(codeTypeDeclaration);
                }

                MakeInternalIfNecessary(codeTypeDeclaration);
                tableTypes[type] = codeTypeDeclaration;
            }

            return codeTypeDeclaration;
        }

        private void CreateHelperMethod(CodeTypeDeclaration codeTypeDeclaration, Entry.ResourceTypeDescription typeDesciption, string parameterType)
        {

            /*
                    public static <typeDescription.runtimeType> <typeDescription.helperName> (<parameterType> id)
                    {
                        return (<typeDescription.runtimeType>)<getObjectClass>.GetObject( <codeTypeDeclaration>.ResourceManager, id );
                    }

             for example

                    public static Font GetFont( FontTag id )
                    {
                        return (Font)Microsoft.SPOT.ResourcesUtility.GetObject( MyResources.ResourceManager, FontTag id );
                    }

            */

            CodeMemberMethod method = new CodeMemberMethod();
            CodeParameterDeclarationExpression parameterIdDeclaration = new CodeParameterDeclarationExpression(parameterType, "id");
            CodeVariableReferenceExpression parameterIdReference = new CodeVariableReferenceExpression(parameterIdDeclaration.Name);
            codeTypeDeclaration.Members.Add(method);

            method.Name = typeDesciption.helperName;
            method.Parameters.Add(parameterIdDeclaration);
            method.ReturnType = new CodeTypeReference(typeDesciption.runtimeType);

            method.Attributes = MemberAttributes.Public | MemberAttributes.Static;
            MakeInternalIfNecessary(method);

            string getObjectClass = this.isMscorlib ? "System.Resources.ResourceManager" : "nanoFramework.Runtime.Native.ResourceUtility";

            CodeVariableReferenceExpression expresionId = new CodeVariableReferenceExpression("id");
            CodeTypeReferenceExpression spotResourcesReference = new CodeTypeReferenceExpression(getObjectClass);
            CodePropertyReferenceExpression resourceManagerReference = new CodePropertyReferenceExpression(null, "ResourceManager");
            CodeExpression expressionGetObject = new CodeMethodInvokeExpression(spotResourcesReference, "GetObject", resourceManagerReference, expresionId);
            CodeExpression expressionValue = new CodeCastExpression(typeDesciption.runtimeType, expressionGetObject);
            CodeMethodReturnStatement statementReturn = new CodeMethodReturnStatement(expressionValue);
            method.Statements.Add(statementReturn);
        }

        private void MakeInternalIfNecessary(CodeTypeDeclaration codeTypeDeclaration)
        {
            if (this.GenerateInternalClass)
            {
                codeTypeDeclaration.TypeAttributes &= ~TypeAttributes.VisibilityMask;
                codeTypeDeclaration.TypeAttributes |= TypeAttributes.NestedAssembly;
            }
        }

        private void MakeInternalIfNecessary(CodeTypeMember codeTypeMember)
        {
            if (this.GenerateInternalClass)
            {
                codeTypeMember.Attributes &= ~MemberAttributes.AccessMask;
                codeTypeMember.Attributes |= MemberAttributes.Assembly;
            }
        }


        private CodeCompileUnit CreateStronglyTypedResourceFile(string resourceName, ArrayList resources, string className, string ns, CodeDomProvider provider, out string[] errors)
        {
            //create list of classes needed to be emitted.
            CodeCompileUnit ccu = new CodeCompileUnit();

            Hashtable tableNamespaces = new Hashtable();
            Hashtable tableTypes = new Hashtable();
            Hashtable tableHelperFunctionsNeeded = new Hashtable();
            ArrayList[] resourceTypesUsed = new ArrayList[TinyResourceFile.ResourceHeader.RESOURCE_Max + 1];

            CodeNamespace codeNamespace;
            CodeTypeDeclaration codeTypeDeclaration;

            //break down resources by enum
            for (int iEntry = 0; iEntry < resources.Count; iEntry++)
            {
                Entry entry = (Entry)resources[iEntry];

                if (resourceTypesUsed[entry.ResourceType] == null)
                {
                    resourceTypesUsed[entry.ResourceType] = new ArrayList();
                }

                if (!resourceTypesUsed[entry.ResourceType].Contains(entry.ClassName))
                {
                    resourceTypesUsed[entry.ResourceType].Add(entry.ClassName);
                }

                codeNamespace = CreateNamespace(ccu, entry.Namespace, tableNamespaces);
                codeTypeDeclaration = CreateTypeDeclaration(codeNamespace, entry.ClassName, tableTypes);

                if (!codeTypeDeclaration.IsEnum)
                {
                    //only initialize once
                    codeTypeDeclaration.IsEnum = true;
                    codeTypeDeclaration.CustomAttributes.Add(new CodeAttributeDeclaration("System.SerializableAttribute"));
                    codeTypeDeclaration.BaseTypes.Add(new CodeTypeReference(typeof(short)));
                }

                CodeMemberField codeMemberField = new CodeMemberField(entry.ClassName, entry.Field);
                codeMemberField.Attributes = MemberAttributes.Const | MemberAttributes.Static;

                CodePrimitiveExpression codeExpression = new CodePrimitiveExpression(entry.Id);
                codeMemberField.InitExpression = codeExpression;
                //set constant value??!!
                int iField = codeTypeDeclaration.Members.Add(codeMemberField);

            }

            //emit helper functions

            codeNamespace = CreateNamespace(ccu, ns, tableNamespaces);
            codeTypeDeclaration = CreateTypeDeclaration(codeNamespace, className, tableTypes);
            MakeInternalIfNecessary(codeTypeDeclaration);

            CodeTypeReferenceExpression codeTypeReferenceExpression = new CodeTypeReferenceExpression(codeTypeDeclaration.Name);

            //private static System.Resources.ResourceManager manager;
            CodeMemberField fieldManager = new CodeMemberField("System.Resources.ResourceManager", "manager");
            CodeFieldReferenceExpression fieldManagerExpression = new CodeFieldReferenceExpression(codeTypeReferenceExpression, fieldManager.Name);

            fieldManager.Attributes = MemberAttributes.Static | MemberAttributes.Private;
            codeTypeDeclaration.Members.Add(fieldManager);

            /*
                    [private|internal] static System.Resources.ResourceManager ResourceManager
                    {
                    get
                    {
                        if(manager == null)
                        {
                            manager = new System.Resources.ResourceManager( "Resources", typeof( Resources ).Assembly );
                        }

                        return manager;
                    }
                }
            */

            CodeBinaryOperatorExpression getResourceManagerExpressionIfNull = new CodeBinaryOperatorExpression(fieldManagerExpression, CodeBinaryOperatorType.IdentityEquality, new CodePrimitiveExpression(null));
            CodeObjectCreateExpression getResourceManagerExpressionNewManager = new CodeObjectCreateExpression(
                "System.Resources.ResourceManager",
                new CodePrimitiveExpression(resourceName),
                new CodePropertyReferenceExpression(new CodeTypeOfExpression(codeTypeReferenceExpression.Type), "Assembly")
                );
            CodeAssignStatement getResourceManagerExpressionInitializeManager = new CodeAssignStatement(fieldManagerExpression, getResourceManagerExpressionNewManager);

            CodeStatementCollection getResourceManagerExpression = new CodeStatementCollection();

            getResourceManagerExpression.AddRange(new CodeStatement[] {
                new CodeConditionStatement( getResourceManagerExpressionIfNull, getResourceManagerExpressionInitializeManager ),
                new CodeMethodReturnStatement( fieldManagerExpression )
                }
            );

            CodeMemberProperty propertyResourceManager = new CodeMemberProperty();
            propertyResourceManager.Name = "ResourceManager";
            propertyResourceManager.Type = new CodeTypeReference("System.Resources.ResourceManager");
            propertyResourceManager.Attributes = MemberAttributes.Public | MemberAttributes.Static;
            MakeInternalIfNecessary(propertyResourceManager);
            propertyResourceManager.GetStatements.AddRange(getResourceManagerExpression);
            codeTypeDeclaration.Members.Add(propertyResourceManager);

            for (int i = 0; i < resourceTypesUsed.Length; i++)
            {
                ArrayList list = resourceTypesUsed[i];

                if (list != null)
                {
                    Entry.ResourceTypeDescription typeDescription = Entry.ResourceTypeDescriptionFromResourceType((byte)i);

                    for (int iClassName = 0; iClassName < list.Count; iClassName++)
                    {
                        CreateHelperMethod(codeTypeDeclaration, typeDescription, (string)list[iClassName]);
                    }
                }
            }

            errors = new string[0];
            return ccu;
        }
        /// <summary>
        /// If no strongly typed resource class filename was specified, we come up with a default based on the
        /// input file name and the default language extension. Broken out here so it can be called from GenerateResource class.
        /// </summary>
        /// <param name="provider">A CodeDomProvider for the language</param>
        /// <param name="outputResourcesFile">Name of the output resources file</param>
        /// <returns>Filename for strongly typed resource class</returns>
        public static string GenerateDefaultStronglyTypedFilename(CodeDomProvider provider, string outputResourcesFile)
        {
            return Path.ChangeExtension(outputResourcesFile, provider.FileExtension);
        }

        /// <summary>
        /// Read resources from an XML or binary format file
        /// </summary>
        /// <param name="reader">Appropriate IResourceReader</param>
        /// <param name="fileName">Filename, for error messages</param>
        private void ReadResources(IResourceReader reader, String fileName)
        {
            using (reader)
            {
                IDictionaryEnumerator resEnum = reader.GetEnumerator();
                while (resEnum.MoveNext())
                {
                    string name = (string)resEnum.Key;
                    // Replace dot in the name with underscore. 
                    // 1. First reason  - this is what desktop resource generator does.
                    // 2. Second reason - Extra dots causes resource generator to create name space and enumerations.
                    //    This complicates the syntax and finally create invalid code if 2 or more dots are present.
                    //    So we just make longer name.
                    name = name.Replace('.', '_');
                    object value = resEnum.Value;
                    AddResource(name, value, fileName);
                }
            }

            EnsureResourcesIds(this.resources);
        }

        private static short GenerateIdFromResourceName(string s)
        {
            //adapted from BCL implementation

            int hash1 = (5381 << 16) + 5381;
            int hash2 = hash1;

            char[] chars = s.ToCharArray();

            int len = s.Length;

            for (int i = 0; i < len; i++)
            {
                char c = s[i];
                if (i % 2 == 0)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ c;
                }
                else
                {
                    hash2 = ((hash2 << 5) + hash2) ^ c;
                }
            }

            int hash = hash1 + (hash2 * 1566083941);

            short ret = (short)((short)(hash >> 16) ^ (short)hash);

            return ret;
        }

        internal static void EnsureResourcesIds(ArrayList resources)
        {
            int iResource;
            Hashtable table = new Hashtable();

            if (resources.Count > UInt16.MaxValue)
            {
                throw new ApplicationException("Too many resources.  Maximum number of resources per ResourceSet is 65535");
            }

            for (iResource = 0; iResource < resources.Count; iResource++)
            {
                Entry entry = (Entry)resources[iResource];

                short id = entry.Id;

                if (table.ContainsKey(id))
                {
                    //rwolff -- check regarding boxed objects....

                    //duplicate id detected.
                    Entry entryDup = (Entry)table[id];

                    throw new ApplicationException(string.Format("Duplicate id detected.  Resources '{0}' and '{1}' are generating the same id=0x{2}", entry.Name, entryDup.Name, id));
                }

                table[id] = entry;
            }

            resources.Sort();
        }

        /// <summary>
        /// Read resources from a text format file
        /// </summary>
        /// <param name="fileName">Input resources filename</param>
        private void ReadTextResources(String fileName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Write resources to an XML or binary format resources file.
        /// </summary>
        /// <remarks>Closes writer automatically</remarks>
        /// <param name="writer">Appropriate IResourceWriter</param>
        private void WriteResources(IResourceWriter writer)
        {
            try
            {
                foreach (Entry entry in resources)
                {
                    string key = entry.RawName;
                    object value = entry.Value;

                    if (writer is TinyResourceWriter)
                    {
                        ((TinyResourceWriter)writer).AddResource(entry);
                    }
                    else
                    {
                        writer.AddResource(key, value);
                    }
                }

                writer.Generate();
            }
            finally
            {
                writer.Close();
            }
        }

        /// <summary>
        /// Write resources to a text format resources file
        /// </summary>
        /// <param name="fileName">Output resources file</param>
        private void WriteTextResources(String fileName)
        {
            using (StreamWriter writer = new StreamWriter(fileName, false, Encoding.UTF8))
            {
                foreach (Entry entry in resources)
                {
                    String key = entry.Name;
                    Object v = entry.Value;
                    String value = v as String;
                    if (value == null)
                    {
                        logger.LogError(null, fileName, 0, 0, 0, 0, "GenerateResource.OnlyStringsSupported", key, v.GetType().FullName);
                    }
                    else
                    {
                        // Escape any special characters in the String.
                        value = value.Replace("\\", "\\\\");
                        value = value.Replace("\n", "\\n");
                        value = value.Replace("\r", "\\r");
                        value = value.Replace("\t", "\\t");

                        writer.WriteLine("{0}={1}", key, value);
                    }
                }
            }
        }

        /// <summary>
        /// Add a resource from a text file to the internal data structures
        /// </summary>
        /// <param name="name">Resource name</param>
        /// <param name="value">Resource value</param>
        /// <param name="inputFileName">Input file for messages</param>
        /// <param name="lineNumber">Line number for messages</param>
        /// <param name="linePosition">Column number for messages</param>
        private void AddResource(string name, object value, String inputFileName, int lineNumber, int linePosition)
        {
            Entry entry = Entry.CreateEntry(name, value, this.stronglyTypedNamespace, this.useNestedClassForEnums ? this.stronglyTypedClassName : string.Empty);

            Debug.Assert(entry.ClassName.Length > 0);

            resources.Add(entry);
        }

        private void AddResource(string name, object value, String inputFileName)
        {
            AddResource(name, value, inputFileName, 0, 0);
        }

        /// <summary>
        /// Name value resource pair to go in resources list
        /// </summary>
        internal abstract class Entry : IComparable
        {
            public class ResourceTypeDescription
            {
                public byte resourceType;
                public string helperName;
                public string runtimeType;
                public string defaultEnum;

                public ResourceTypeDescription(byte resourceType, string helperName, string runtimeType, string defaultEnum)
                {
                    this.resourceType = resourceType;
                    this.helperName = helperName;
                    this.runtimeType = runtimeType;
                    this.defaultEnum = defaultEnum;
                }
            }

            private static ResourceTypeDescription[] typeDescriptions = new ResourceTypeDescription[]
                {
                    null, //RESOURCE_Invalid
                    new ResourceTypeDescription(TinyResourceFile.ResourceHeader.RESOURCE_Bitmap, "GetBitmap", "Microsoft.SPOT.Bitmap", "BitmapResources"),
                    new ResourceTypeDescription(TinyResourceFile.ResourceHeader.RESOURCE_Font, "GetFont", "Microsoft.SPOT.Font", "FontResources"),
                    new ResourceTypeDescription(TinyResourceFile.ResourceHeader.RESOURCE_String, "GetString", "System.String", "StringResources"),
                    new ResourceTypeDescription(TinyResourceFile.ResourceHeader.RESOURCE_Binary, "GetBytes", "System.Byte[]", "BinaryResources"),
                    };

            public static ResourceTypeDescription ResourceTypeDescriptionFromResourceType(byte type)
            {
                if (type < TinyResourceFile.ResourceHeader.RESOURCE_Bitmap || type > TinyResourceFile.ResourceHeader.RESOURCE_Binary)
                {
                    throw new ArgumentException();
                }

                return typeDescriptions[type];
            }

            public static Entry CreateEntry(string name, object value, string defaultNamespace, string defaultDeclaringClass)
            {
                string stringValue = value as string;
                System.Drawing.Bitmap bitmapValue = value as System.Drawing.Bitmap;
                byte[] rawValue = value as byte[];
                Entry entry = null;

                if (stringValue != null)
                {
                    entry = new StringEntry(name, stringValue);
                }
                else if (bitmapValue != null)
                {
                    entry = new BitmapEntry(name, bitmapValue);
                }
                if (rawValue != null)
                {
                    entry = TinyResourcesEntry.TryCreateTinyResourcesEntry(name, rawValue);

                    if (entry == null)
                    {
                        entry = new BinaryEntry(name, rawValue);
                    }
                }

                if (entry == null)
                {
                    throw new Exception();
                }

                if (entry.Namespace.Length == 0)
                {
                    entry.Namespace = defaultNamespace;
                }

                if (entry.ClassName.Length == 0)
                {
                    ResourceTypeDescription typeDescription = ResourceTypeDescriptionFromResourceType(entry.ResourceType);
                    entry.ClassName = typeDescription.defaultEnum;

                    if (!string.IsNullOrEmpty(defaultDeclaringClass))
                    {
                        entry.ClassName = string.Format("{0}+{1}", defaultDeclaringClass, entry.ClassName);
                    }
                }

                return entry;
            }

            private bool ParseId(string val, out short id)
            {
                val = val.Trim();
                bool fSuccess = false;

                if (val.StartsWith("0x", true, CultureInfo.InvariantCulture))
                {
                    ushort us;

                    fSuccess = ushort.TryParse(val.Substring(2), NumberStyles.AllowHexSpecifier, null, out us);

                    id = (short)us;
                }
                else
                {
                    fSuccess = short.TryParse(val, out id);
                }

                return fSuccess;
            }

            public Entry(string name, object value)
            {
                this.value = value;
                this.ns = string.Empty;
                this.className = string.Empty;
                this.field = string.Empty;
                this.rawName = name;

                //parse name

                string[] tokens = name.Split(';');
                string idValue;
                short idT;

                switch (tokens.Length)
                {
                    case 1:
                        name = tokens[0];
                        idValue = string.Empty;
                        break;
                    case 2:
                        name = tokens[0];
                        idValue = tokens[1];
                        break;
                    default:
                        throw new ArgumentException();
                }

                idValue = idValue.Trim();

                if (idValue.Length > 0)
                {
                    if (!ParseId(idValue, out idT))
                    {
                        throw new ApplicationException(string.Format("Cannot parse id '{0}' from resource '{1}'", idValue, name));
                    }

                    this.Id = idT;
                }
                else
                {
                    this.Id = GenerateIdFromResourceName(name);
                }

                int iDotLast = name.LastIndexOf('.');

                this.field = name;

                if (iDotLast >= 0)
                {
                    this.field = name.Substring(iDotLast + 1);

                    name = name.Substring(0, iDotLast);

                    iDotLast = name.LastIndexOf('.');
                    //iDotLast = name.LastIndexOfAny( new char[] { '.', '+' } );

                    this.className = name.Trim();

                    if (iDotLast >= 0)
                    {
                        this.className = name.Substring(iDotLast + 1).Trim();
                        this.ns = name.Substring(0, iDotLast).Trim();
                    }
                }
            }

            private string ns;
            private string className;
            private string field;
            private short id;
            private object value;
            private string rawName;

            public string Name
            {
                get { return string.Format("{0}.{1}.{2};0x{3}", ns, className, field, id.ToString("X4")); }
            }

            public string RawName
            {
                get { return rawName; }
            }

            public object Value
            {
                get { return value; }
            }

            public short Id
            {
                get { return id; }
                set { id = value; }
            }

            public string Namespace
            {
                get { return ns; }
                set { ns = value; }
            }

            public string ClassName
            {
                get { return className; }
                set { className = value; }
            }

            public string Field
            {
                get { return field; }
                set { field = value; }
            }

            #region IComparable Members

            int IComparable.CompareTo(object obj)
            {
                Entry entry = obj as Entry;

                if (entry == null)
                {
                    return -1;
                }

                return this.id.CompareTo(entry.id);
            }

            #endregion

            public virtual byte ResourceType
            {
                get
                {
                    if (value.GetType() == typeof(string)) return TinyResourceFile.ResourceHeader.RESOURCE_String;

                    return TinyResourceFile.ResourceHeader.RESOURCE_Invalid;
                }
            }

            public virtual byte[] GenerateResourceData()
            {
                return null;
            }
        }

        private class StringEntry : Entry
        {
            public StringEntry(string name, string value) : base(name, value)
            {
            }

            private string StringValue
            {
                get { return this.Value as string; }
            }

            public override byte ResourceType
            {
                get
                {
                    return TinyResourceFile.ResourceHeader.RESOURCE_String;
                }
            }

            public override byte[] GenerateResourceData()
            {
                string val = this.StringValue + '\0';

                byte[] data = Encoding.UTF8.GetBytes(val);

                return data;
            }
        }

        private class BitmapEntry : Entry
        {
            public BitmapEntry(string name, System.Drawing.Bitmap value) : base(name, value)
            {
            }

            private System.Drawing.Bitmap BitmapValue
            {
                get { return this.Value as System.Drawing.Bitmap; }
            }

            public override byte ResourceType
            {
                get
                {
                    return TinyResourceFile.ResourceHeader.RESOURCE_Bitmap;
                }
            }


            private void Adjust1bppOrientation(byte[] buf)
            {
                //CLR_GFX_Bitmap::AdjustBitOrientation
                //The TinyCLR treats 1bpp bitmaps reversed from Windows
                //And most likely every other 1bpp format as well
                byte[] reverseTable = new byte[]
            {
                0x00, 0x80, 0x40, 0xC0, 0x20, 0xA0, 0x60, 0xE0,
                0x10, 0x90, 0x50, 0xD0, 0x30, 0xB0, 0x70, 0xF0,
                0x08, 0x88, 0x48, 0xC8, 0x28, 0xA8, 0x68, 0xE8,
                0x18, 0x98, 0x58, 0xD8, 0x38, 0xB8, 0x78, 0xF8,
                0x04, 0x84, 0x44, 0xC4, 0x24, 0xA4, 0x64, 0xE4,
                0x14, 0x94, 0x54, 0xD4, 0x34, 0xB4, 0x74, 0xF4,
                0x0C, 0x8C, 0x4C, 0xCC, 0x2C, 0xAC, 0x6C, 0xEC,
                0x1C, 0x9C, 0x5C, 0xDC, 0x3C, 0xBC, 0x7C, 0xFC,
                0x02, 0x82, 0x42, 0xC2, 0x22, 0xA2, 0x62, 0xE2,
                0x12, 0x92, 0x52, 0xD2, 0x32, 0xB2, 0x72, 0xF2,
                0x0A, 0x8A, 0x4A, 0xCA, 0x2A, 0xAA, 0x6A, 0xEA,
                0x1A, 0x9A, 0x5A, 0xDA, 0x3A, 0xBA, 0x7A, 0xFA,
                0x06, 0x86, 0x46, 0xC6, 0x26, 0xA6, 0x66, 0xE6,
                0x16, 0x96, 0x56, 0xD6, 0x36, 0xB6, 0x76, 0xF6,
                0x0E, 0x8E, 0x4E, 0xCE, 0x2E, 0xAE, 0x6E, 0xEE,
                0x1E, 0x9E, 0x5E, 0xDE, 0x3E, 0xBE, 0x7E, 0xFE,
                0x01, 0x81, 0x41, 0xC1, 0x21, 0xA1, 0x61, 0xE1,
                0x11, 0x91, 0x51, 0xD1, 0x31, 0xB1, 0x71, 0xF1,
                0x09, 0x89, 0x49, 0xC9, 0x29, 0xA9, 0x69, 0xE9,
                0x19, 0x99, 0x59, 0xD9, 0x39, 0xB9, 0x79, 0xF9,
                0x05, 0x85, 0x45, 0xC5, 0x25, 0xA5, 0x65, 0xE5,
                0x15, 0x95, 0x55, 0xD5, 0x35, 0xB5, 0x75, 0xF5,
                0x0D, 0x8D, 0x4D, 0xCD, 0x2D, 0xAD, 0x6D, 0xED,
                0x1D, 0x9D, 0x5D, 0xDD, 0x3D, 0xBD, 0x7D, 0xFD,
                0x03, 0x83, 0x43, 0xC3, 0x23, 0xA3, 0x63, 0xE3,
                0x13, 0x93, 0x53, 0xD3, 0x33, 0xB3, 0x73, 0xF3,
                0x0B, 0x8B, 0x4B, 0xCB, 0x2B, 0xAB, 0x6B, 0xEB,
                0x1B, 0x9B, 0x5B, 0xDB, 0x3B, 0xBB, 0x7B, 0xFB,
                0x07, 0x87, 0x47, 0xC7, 0x27, 0xA7, 0x67, 0xE7,
                0x17, 0x97, 0x57, 0xD7, 0x37, 0xB7, 0x77, 0xF7,
                0x0F, 0x8F, 0x4F, 0xCF, 0x2F, 0xAF, 0x6F, 0xEF,
                0x1F, 0x9F, 0x5F, 0xDF, 0x3F, 0xBF, 0x7F,0xFF,
                };

                for (int i = buf.Length - 1; i >= 0; i--)
                {
                    buf[i] = reverseTable[buf[i]];
                }
            }

            private void Compress1bpp(TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription, ref byte[] buf)
            {
                MemoryStream ms = new MemoryStream(buf.Length);

                //adapted from CLR_GFX_Bitmap::Compress
                //CLR_RT_Buffer   buffer;
                int count = 0;
                bool fSetSav = false;
                bool fSet = false;
                bool fFirst = true;
                bool fRun = true;
                byte data = 0;
                bool fEmit = false;
                int widthInWords = (int)((bitmapDescription.m_width + 31) / 32);
                int iByte;
                byte iPixelMask;

                iByte = 0;
                for (int y = 0; y < bitmapDescription.m_height; y++)
                {
                    iPixelMask = 0x1;
                    iByte = y * (widthInWords * 4);

                    for (int x = 0; x < bitmapDescription.m_width; x++)
                    {
                        fSetSav = fSet;

                        fSet = (buf[iByte] & iPixelMask) != 0;
                        if (fFirst)
                        {
                            fFirst = false;
                        }
                        else
                        {
                            if (fRun)
                            {
                                fRun = (fSetSav == fSet);

                                if ((count == 0x3f + TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunOffset) ||
                                (!fRun && count >= TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunOffset))
                                {
                                    data = TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRun;
                                    data |= (fSetSav ? TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunSet : (byte)0x0);
                                    data |= (byte)(count - TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunOffset);
                                    fEmit = true;
                                }
                            }

                            if (!fRun && count == TinyResourceFile.CLR_GFX_BitmapDescription.c_UncompressedRunLength)
                            {
                                fEmit = true;
                            }

                            if (fEmit)
                            {
                                ms.WriteByte(data);

                                data = 0;
                                count = 0;
                                fEmit = false;
                                fRun = true;
                            }
                        }

                        data |= (byte)((0x1 << count) & (fSet ? 0xff : 0x0));

                        iPixelMask <<= 1;
                        if (iPixelMask == 0)
                        {
                            iPixelMask = 0x1;
                            iByte++;
                        }

                        count++;
                    }
                }

                if (fRun && count >= TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunOffset)
                {
                    data = TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRun;
                    data |= (fSetSav ? TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunSet : (byte)0x0);
                    data |= (byte)(count - TinyResourceFile.CLR_GFX_BitmapDescription.c_CompressedRunOffset);
                }

                ms.WriteByte(data);

                if (ms.Length < buf.Length)
                {
                    ms.Capacity = (int)ms.Length;
                    buf = ms.GetBuffer();

                    bitmapDescription.m_flags |= TinyResourceFile.CLR_GFX_BitmapDescription.c_Compressed;
                }
            }

            private byte[] GetBitmapDataBmp(Bitmap bitmap, out TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription)
            {
                //issue warning for formats that we lose information?
                //other formats that we need to support??

                byte bitsPerPixel = 24;
                BitmapData bitmapData = null;
                Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                PixelFormat formatDst = bitmap.PixelFormat;
                byte[] data = null;

                switch (bitmap.PixelFormat)
                {
                    case PixelFormat.Format1bppIndexed:
                        bitsPerPixel = 1;
                        formatDst = PixelFormat.Format1bppIndexed;
                        break;
                    // Anything more than 16bpp will fall through to 16bpp
                    case PixelFormat.Format8bppIndexed:
                    case PixelFormat.Format24bppRgb:
                    case PixelFormat.Format32bppRgb:
                    case PixelFormat.Format48bppRgb:
                    case PixelFormat.Format16bppRgb555:
                    case PixelFormat.Format16bppRgb565:
                        bitsPerPixel = 16;
                        formatDst = PixelFormat.Format16bppRgb565;
                        break;
                    default:
                        throw new NotSupportedException(string.Format("PixelFormat of '{0}' resource not supported", this.Name));
                }

                //turn bitmap data into a form we can use.

                if (formatDst != bitmap.PixelFormat)
                {
                    bitmap = bitmap.Clone(rect, formatDst);
                }

                try
                {
                    bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, formatDst);

                    IntPtr p = bitmapData.Scan0;
                    data = new byte[bitmapData.Stride * bitmap.Height];

                    System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, data, 0, data.Length);

                    if (bitsPerPixel == 1)
                    {
                        //special case for 1pp with index 0 equals white???!!!???
                        if (bitmap.Palette.Entries[0].GetBrightness() < 0.5)
                        {
                            for (int i = 0; i < data.Length; i++)
                            {
                                data[i] = (byte)~data[i];
                            }
                        }

                        //special case for 1pp need to flip orientation??
                        //for some stupid reason, 1bpp is flipped compared to windows!!
                        Adjust1bppOrientation(data);
                    }
                }
                finally
                {
                    if (bitmapData != null)
                    {
                        bitmap.UnlockBits(bitmapData);
                    }
                }

                bitmapDescription = new TinyResourceFile.CLR_GFX_BitmapDescription((ushort)bitmap.Width, (ushort)bitmap.Height, 0, bitsPerPixel, TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeBitmap);

                if (bitsPerPixel == 1)
                {
                    //test compression;
                    Compress1bpp(bitmapDescription, ref data);
                }

                return data;
            }

            private byte[] GetBitmapDataRaw(Bitmap bitmap, out TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription, byte type)
            {
                bitmapDescription = new TinyResourceFile.CLR_GFX_BitmapDescription((ushort)bitmap.Width, (ushort)bitmap.Height, 0, 1, type);

                MemoryStream stream = new MemoryStream();
                bitmap.Save(stream, bitmap.RawFormat);

                stream.Capacity = (int)stream.Length;
                return stream.GetBuffer();
            }

            private byte[] GetBitmapDataJpeg(Bitmap bitmap, out TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription)
            {
                return GetBitmapDataRaw(bitmap, out bitmapDescription, TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeJpeg);
            }

            private byte[] GetBitmapDataGif(Bitmap bitmap, out TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription)
            {
                return GetBitmapDataRaw(bitmap, out bitmapDescription, TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeGif);
            }

            private byte[] GetBitmapData(Bitmap bitmap, out TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription)
            {
                byte[] data = null;

                if (bitmap.Width > 0xFFFF || bitmap.Height > 0xFFFF)
                {
                    throw new ArgumentException("bitmap dimensions out of range");
                }

                if (bitmap.RawFormat.Equals(ImageFormat.Jpeg))
                {
                    data = GetBitmapDataJpeg(bitmap, out bitmapDescription);
                }
                else if (bitmap.RawFormat.Equals(ImageFormat.Gif))
                {
                    data = GetBitmapDataGif(bitmap, out bitmapDescription);
                }
                else if (bitmap.RawFormat.Equals(ImageFormat.Bmp))
                {
                    data = GetBitmapDataBmp(bitmap, out bitmapDescription);
                }
                else
                {
                    throw new NotSupportedException(string.Format("Bitmap imageFormat not supported '{0}'", bitmap.RawFormat.Guid.ToString()));
                }

                return data;
            }

            public override byte[] GenerateResourceData()
            {
                Bitmap bitmap = this.BitmapValue;

                TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription;

                byte[] data = GetBitmapData(bitmap, out bitmapDescription);

                MemoryStream stream = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(stream);

                bitmapDescription.Serialize(writer);
                writer.Write(data);

                stream.Capacity = (int)stream.Length;
                return stream.GetBuffer();
            }

            /*
            public override byte[] GenerateResourceData()
            {
                byte[] data = null;

                ushort flags = 0;
                byte type = 0;
                byte bitsPerPixel = 24;
                BitmapData bitmapData = null;
                Bitmap bitmap = this.BitmapValue;
                PixelFormat formatDst = bitmap.PixelFormat;
                ushort width = (ushort)bitmap.Width;
                ushort height = (ushort)bitmap.Height;
                TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription;

                Rectangle rect = new Rectangle( 0, 0, this.BitmapValue.Width, this.BitmapValue.Height );

                if(bitmap.RawFormat.Equals(ImageFormat.Jpeg))
                {
                    type = TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeJpeg;
                }
                else if(bitmap.RawFormat.Equals(ImageFormat.Gif))
                {
                    type = TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeGif;
                }
                else if(bitmap.RawFormat.Equals(ImageFormat.Bmp))
                {
                    type = TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeBitmap;

                    //issue warning for formats that we lose information?
                    //other formats that we need to support??

                    switch(bitmap.PixelFormat)
                    {
                        case PixelFormat.Format1bppIndexed:
                            bitsPerPixel = 1;
                            formatDst = PixelFormat.Format1bppIndexed;
                            break;
                        case PixelFormat.Format24bppRgb:
                        case PixelFormat.Format32bppRgb:
                        case PixelFormat.Format48bppRgb:
                        //currently don't support more than 16bp...fall through..
                        case PixelFormat.Format16bppRgb555:
                        case PixelFormat.Format16bppRgb565:
                            bitsPerPixel = 16;
                            formatDst = PixelFormat.Format16bppRgb565;
                            break;
                        default:
                            throw new NotSupportedException( string.Format( "PixelFormat of '{0}' resource not supported", this.Name ) );
                    }

                    //turn bitmap data into a form we can use.

                    if(formatDst != bitmap.PixelFormat)
                    {
                        bitmap = bitmap.Clone( rect, formatDst );
                    }
                }
                else
                {
                    throw new NotSupportedException( string.Format("Bitmap imageFormat not supported '{0}'", bitmap.RawFormat.Guid.ToString()) );
                }

                try
                {
                    bitmapData = bitmap.LockBits( rect, ImageLockMode.ReadOnly, formatDst );

                    IntPtr p = bitmapData.Scan0;
                    data = new byte[bitmapData.Stride * height];

                    System.Runtime.InteropServices.Marshal.Copy( bitmapData.Scan0, data, 0, data.Length );

                    if(bitsPerPixel == 1)
                    {
                        //special case for 1pp with index 0 equals white???!!!???
                        if(bitmap.Palette.Entries[0].GetBrightness() < 0.5)
                        {
                            for(int i = 0; i < data.Length; i++)
                            {
                                data[i] = (byte)~data[i];
                            }
                        }

                        //special case for 1pp need to flip orientation??
                        //for some stupid reason, 1bpp is flipped compared to windows!!
                        Adjust1bppOrientation( data );
                    }
                }
                finally
                {
                    if(bitmapData != null)
                    {
                        bitmap.UnlockBits( bitmapData );
                    }
                }

                TinyResourceFile.CLR_GFX_BitmapDescription bitmapDescription = new TinyResourceFile.CLR_GFX_BitmapDescription( width, height, flags, bitsPerPixel, type );

                if(bitsPerPixel == 1 && type == TinyResourceFile.CLR_GFX_BitmapDescription.c_TypeBitmap)
                {
                    //test compression;
                    Compress1bpp( bitmapDescription, ref data );
                }

                MemoryStream stream = new MemoryStream();
                BinaryWriter writer = new BinaryWriter( stream );

                bitmapDescription.Serialize( writer );
                writer.Write( data );

                byte[] buf = new byte[stream.Length];

                Array.Copy( stream.GetBuffer(), buf, stream.Length );

                return buf;
            }
            */
        }

        private class TinyResourcesEntry : Entry
        {
            TinyResourceFile.ResourceHeader resource;

            public TinyResourcesEntry(string name, byte[] value) : base(name, value)
            {
            }

            public static TinyResourcesEntry TryCreateTinyResourcesEntry(string name, byte[] value)
            {
                TinyResourcesEntry entry = null;

                try
                {
                    MemoryStream stream = new MemoryStream(value);

                    BinaryReader reader = new BinaryReader(stream);
                    uint magicNumber = reader.ReadUInt32();

                    stream.Position = 0;

                    if (magicNumber == TinyResourceFile.Header.MAGIC_NUMBER)
                    {
                        TinyResourceFile file = new TinyResourceFile();

                        file.Deserialize(reader);

                        if (file.resources.Length == 1)
                        {
                            TinyResourceFile.Resource resource = file.resources[0];

                            entry = new TinyResourcesEntry(name, resource.data);
                            entry.resource = resource.header;
                        }
                    }
                }
                catch
                {
                }

                return entry;
            }

            private byte[] RawValue
            {
                get { return this.Value as byte[]; }
            }

            public override byte ResourceType
            {
                get
                {
                    return resource.kind;
                }
            }

            public override byte[] GenerateResourceData()
            {
                return this.RawValue;
            }

        }

        private class BinaryEntry : Entry
        {
            public BinaryEntry(string name, byte[] value) : base(name, value)
            {

            }

            private byte[] BinaryValue
            {
                get { return this.Value as byte[]; }
            }

            public override byte ResourceType
            {
                get
                {
                    return TinyResourceFile.ResourceHeader.RESOURCE_Binary;
                }
            }

            public override byte[] GenerateResourceData()
            {
                return this.BinaryValue;
            }
        }

        #endregion // Code from ResGen.EXE

        internal class TinyResourceFile
        {
            /*
                    .tinyresources file format.  The Header is shared with the Metadataprocessor.  Everything else
                    is shared with the TinyCLR>

                    -------
                    Header
                    -------

                    uint MagicNumber = 0xf995b0a8;
                    uint Version;
                    uint SizeOfHeader;
                    uint SizeOfResourceHeader;          //size of all CLR_RECORD_RESOURCE structures to follow
                    uint NumberOfResources;             //number of resources

                    ---------------------
                    Resource Headers
                    ---------------------

                    Starting at Header.SizeOfHeader in the stream,
                    Header.NumberOfResources resourceHeader structures



            */

            public Header header;
            public Resource[] resources;

            public class Resource
            {
                public ResourceHeader header;
                public byte[] data;

                public Resource(ResourceHeader header, byte[] data)
                {
                    this.header = header;
                    this.data = data;
                }
            }

            public TinyResourceFile()
            {
                resources = new Resource[0];
            }

            public TinyResourceFile(Header header) : this()
            {
                this.header = header;
            }

            public void AddResource(Resource resource)
            {
                int cResource = resources.Length;

                Resource[] resourcesNew = new Resource[cResource + 1];
                resources.CopyTo(resourcesNew, 0);
                resourcesNew[cResource] = resource;
                resources = resourcesNew;
            }

            public void Serialize(BinaryWriter writer)
            {
                this.header.Serialize(writer);

                for (int iResource = 0; iResource < this.resources.Length; iResource++)
                {
                    Resource resource = resources[iResource];

                    resource.header.Serialize(writer);
                    writer.Write(resource.data);
                }
            }

            public void Deserialize(BinaryReader reader)
            {
                header = new Header();
                header.Deserialize(reader);

                if (header.NumberOfResources == 0)
                {
                    throw new SerializationException("No resources found");
                }

                resources = new Resource[header.NumberOfResources];

                for (int iResource = 0; iResource < resources.Length; iResource++)
                {
                    Resource resource = new Resource(new ResourceHeader(), new byte[0]);

                    resources[iResource] = resource;
                    resource.header.Deserialize(reader);
                    resource.data = reader.ReadBytes((int)resource.header.size);
                }
            }

            #region Records

            public class Header
            {
                public const uint VERSION = 2;
                public const uint MAGIC_NUMBER = 0xf995b0a8;
                public const uint SIZE_FILE_HEADER = 5 * 4;
                public const uint SIZE_RESOURCE_HEADER = 2 * 4;

                public uint MagicNumber;
                public uint Version;
                public uint SizeOfHeader;
                public uint SizeOfResourceHeader;
                public uint NumberOfResources;

                public Header(uint numResources)
                {
                    this.MagicNumber = MAGIC_NUMBER;
                    this.Version = VERSION;
                    this.SizeOfHeader = SIZE_FILE_HEADER;
                    this.SizeOfResourceHeader = SIZE_RESOURCE_HEADER;
                    this.NumberOfResources = numResources;
                }

                public Header()
                {
                }

                public void Serialize(BinaryWriter writer)
                {
                    writer.Write(MagicNumber);
                    writer.Write(Version);
                    writer.Write(SizeOfHeader);
                    writer.Write(SizeOfResourceHeader);
                    writer.Write(NumberOfResources);
                }

                public void Deserialize(BinaryReader reader)
                {
                    this.MagicNumber = reader.ReadUInt32();
                    this.Version = reader.ReadUInt32();
                    this.SizeOfHeader = reader.ReadUInt32();
                    this.SizeOfResourceHeader = reader.ReadUInt32();
                    this.NumberOfResources = reader.ReadUInt32();

                    reader.BaseStream.Position = this.SizeOfHeader;

                    if (this.MagicNumber != MAGIC_NUMBER ||
                         this.SizeOfHeader < SIZE_FILE_HEADER ||
                         this.SizeOfResourceHeader < SIZE_RESOURCE_HEADER
                    )
                    {
                        throw new SerializationException();
                    }
                    else if (this.Version != VERSION)
                    {
                        throw new SerializationException(string.Format("Incompatible version (version {0}) found, expecting version {1}.", this.Version, VERSION));
                    }
                }

                public uint OffsetOfResourceData
                {
                    get
                    {
                        return this.SizeOfHeader + this.SizeOfResourceHeader * this.NumberOfResources;
                    }
                }
            }

            public class ResourceHeader
            {
                public const byte RESOURCE_Invalid = 0x00;
                public const byte RESOURCE_Bitmap = 0x01;
                public const byte RESOURCE_Font = 0x02;
                public const byte RESOURCE_String = 0x03;
                public const byte RESOURCE_Binary = 0x04;
                public const byte RESOURCE_Max = RESOURCE_Binary;

                public short id;
                public byte kind;
                public byte pad;
                public uint size;

                public ResourceHeader(short id, byte kind, uint size)
                {
                    this.id = id;
                    this.kind = kind;
                    this.pad = 0;
                    this.size = size;
                }

                public ResourceHeader()
                {
                }

                public void Serialize(BinaryWriter writer)
                {
                    writer.Write(this.id);
                    writer.Write(this.kind);
                    writer.Write(this.pad);
                    writer.Write(this.size);
                }

                public void Deserialize(BinaryReader reader)
                {
                    this.id = reader.ReadInt16();
                    this.kind = reader.ReadByte();
                    this.pad = reader.ReadByte();
                    this.size = reader.ReadUInt32();
                }
            }

            public class CLR_GFX_BitmapDescription
            {
                public const ushort c_ReadOnly = 0x0001;
                public const ushort c_Compressed = 0x0002;

                public const ushort c_Rotation0 = 0x0000;
                public const ushort c_Rotation90 = 0x0004;
                public const ushort c_Rotation180 = 0x0008;
                public const ushort c_Rotation270 = 0x000b;
                public const ushort c_RotationMask = 0x000b;

                public const byte c_CompressedRun = 0x80;
                public const byte c_CompressedRunSet = 0x40;
                public const byte c_CompressedRunLengthMask = 0x3f;
                public const byte c_UncompressedRunLength = 7;
                public const byte c_CompressedRunOffset = c_UncompressedRunLength + 1;

                // Note that these type definitions has to match the ones defined in Bitmap.BitmapImageType enum defined in Graphics.cs
                public const byte c_TypeBitmap = 0;
                public const byte c_TypeGif = 1;
                public const byte c_TypeJpeg = 2;

                // !!!!WARNING!!!!
                // These fields should correspond to CLR_GFX_BitmapDescription in TinyCLR_Graphics.h
                // and should be 4-byte aligned in size. When these fields are changed, the version number
                // of the tinyresource file should be incremented, the tinyfnts should be updated (buildhelper -convertfont ...)
                // and the MMP should also be updated as well. (Consult rwolff before touching this.)
                public uint m_width;
                public uint m_height;
                public ushort m_flags;
                public byte m_bitsPerPixel;
                public byte m_type;

                public CLR_GFX_BitmapDescription(ushort width, ushort height, ushort flags, byte bitsPerPixel, byte type)
                {
                    this.m_width = width;
                    this.m_height = height;
                    this.m_flags = flags;
                    this.m_bitsPerPixel = bitsPerPixel;
                    this.m_type = type;
                }

                public CLR_GFX_BitmapDescription()
                {
                }

                public void Serialize(BinaryWriter writer)
                {
                    writer.Write(m_width);
                    writer.Write(m_height);
                    writer.Write(m_flags);
                    writer.Write(m_bitsPerPixel);
                    writer.Write(m_type);
                }

                public void Deserialize(BinaryReader reader)
                {
                    this.m_width = reader.ReadUInt16();
                    this.m_height = reader.ReadUInt16();
                    this.m_flags = reader.ReadUInt16();
                    this.m_bitsPerPixel = reader.ReadByte();
                    this.m_type = reader.ReadByte();
                }
            }
            #endregion
        }

        internal class TinyResourceWriter : IResourceWriter
        {
            string fileName;
            ArrayList resources;

            public TinyResourceWriter(string fileName)
            {
                this.fileName = fileName;
                this.resources = new ArrayList();
            }

            public void AddResource(Entry entry)
            {
                resources.Add(entry);
            }

            private void Add(string name, object value)
            {
                Entry entry = Entry.CreateEntry(name, value, string.Empty, string.Empty);
                resources.Add(entry);
            }

            #region IResourceWriter Members

            void IResourceWriter.AddResource(string name, byte[] value)
            {
                Add(name, value);
            }

            void IResourceWriter.AddResource(string name, object value)
            {
                Add(name, value);
            }

            void IResourceWriter.AddResource(string name, string value)
            {
                Add(name, value);
            }

            void IResourceWriter.Close()
            {
                ((IDisposable)this).Dispose();
            }

            void IResourceWriter.Generate()
            {
                //PrepareToGenerate();
                ProcessResourceFiles.EnsureResourcesIds(this.resources);

                TinyResourceFile.Header header = new TinyResourceFile.Header((uint)resources.Count);
                TinyResourceFile file = new TinyResourceFile(header);

                for (int iResource = 0; iResource < resources.Count; iResource++)
                {
                    Entry entry = (Entry)resources[iResource];

                    byte[] data = entry.GenerateResourceData();
                    TinyResourceFile.ResourceHeader resource = new TinyResourceFile.ResourceHeader(entry.Id, entry.ResourceType, (uint)data.Length);

                    file.AddResource(new TinyResourceFile.Resource(resource, data));
                }

                using (FileStream fileStream = File.Open(fileName, FileMode.OpenOrCreate))
                {
                    BinaryWriter writer = new BinaryWriter(fileStream);
                    file.Serialize(writer);
                    fileStream.Flush();
                }
            }

            #endregion

            #region IDisposable Members

            void IDisposable.Dispose()
            {
            }

            #endregion
        }

        public class TinyResourceReader : IResourceReader
        {

            #region IResourceReader Members

            void IResourceReader.Close()
            {
                throw new NotImplementedException();
            }

            IDictionaryEnumerator IResourceReader.GetEnumerator()
            {
                throw new NotImplementedException();
            }

            #endregion

            #region IEnumerable Members

            IEnumerator IEnumerable.GetEnumerator()
            {
                throw new NotImplementedException();
            }

            #endregion

            #region IDisposable Members

            void IDisposable.Dispose()
            {
                throw new NotImplementedException();
            }

            #endregion
        }
    }
}
