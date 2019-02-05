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
using nanoFramework.Tools.Utilities;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
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
                    IVSMDCodeDomProvider vsmdCodeDomProvider = (IVSMDCodeDomProvider)GetServiceAsync(CodeDomServiceGuid);
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
                codeDomProvider = value ?? throw new ArgumentNullException();
            }
        }

        private ServiceProvider SiteServiceProvider
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (serviceProvider == null)
                {

                    IOleServiceProvider oleServiceProvider = site as IOleServiceProvider;
                    Debug.Assert(oleServiceProvider != null, "Unable to get IOleServiceProvider from site object.");

                    serviceProvider = new ServiceProvider(oleServiceProvider);
                }
                return serviceProvider;
            }
        }

        protected async System.Threading.Tasks.Task<object> GetServiceAsync(Guid serviceGuid)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return SiteServiceProvider.GetService(serviceGuid);
        }

        protected async System.Threading.Tasks.Task<object> GetServiceAsync(Type serviceType)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

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

    //[ComVisible(true)]
    [Guid(ComponentGuid)]
#pragma warning disable IDE1006 // Naming Styles
    internal class nFResXFileCodeGenerator : BaseCodeGeneratorWithSite, IObjectWithSite
#pragma warning restore IDE1006 // Naming Styles
    {
        public const string Name = nameof(nFResXFileCodeGenerator);
        public const string Description = "nanoFramework code-behind generator for managed resources";
        public const string ComponentGuid = "81EE0274-5CE2-46F2-AC79-7791F3275510";

        private const string DesignerExtension = ".Designer";

        internal bool m_fInternal = true;
        internal bool m_fNestedEnums = true;
        internal bool m_fMscorlib = false;

        protected string GetResourcesNamespace()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string resourcesNamespace = null;
            try
            {
                Guid vsBrowseObjectGuid = typeof(IVsBrowseObject).GUID;

                GetSite(ref vsBrowseObjectGuid, out IntPtr punkVsBrowseObject);

                if (punkVsBrowseObject != IntPtr.Zero)
                {

                    IVsBrowseObject vsBrowseObject = Marshal.GetObjectForIUnknown(punkVsBrowseObject) as IVsBrowseObject;
                    Debug.Assert(vsBrowseObject != null, "Generator invoked by Site that is not IVsBrowseObject?");

                    Marshal.Release(punkVsBrowseObject);

                    if (vsBrowseObject != null)
                    {
                        vsBrowseObject.GetProjectItem(out IVsHierarchy vsHierarchy, out uint vsitemid);

                        Debug.Assert(vsHierarchy != null, "GetProjectItem should have thrown or returned a valid IVsHierarchy");
                        Debug.Assert(vsitemid != 0, "GetProjectItem should have thrown or returned a valid VSITEMID");


                        if (vsHierarchy != null)
                        {
                            vsHierarchy.GetProperty(vsitemid, (int)__VSHPROPID.VSHPROPID_DefaultNamespace, out object obj);
                            if (obj is string objStr)
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
            ext = String.Empty;

            int hResult = base.DefaultExtension(out string baseExtension);

            if (hResult != COM_HResults.S_OK)
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
            ThreadHelper.ThrowIfNotOnUIThread();

            MemoryStream outputStream = new MemoryStream();
            StreamWriter streamWriter = new StreamWriter(outputStream);
            string inputFileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFileName);

            // get VS extension assembly to reach ProcessResourceFiles type
            Assembly buildTasks = GetType().Assembly;

            Type typ = buildTasks.GetType("nanoFramework.Tools.ProcessResourceFiles");

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

                typ.GetMethod("CreateStronglyTypedResources").Invoke(processResourceFiles, new object[] { inputFileName, CodeProvider, streamWriter, resourceName });
            }
            else
            {
                // this shouldn't happen
                MessageCentre.InternalErrorMessage("Exception when generating code-behind file. ProcessResourceFiles type missing. Please reinstall the nanoFramework extension.");
            }

            return base.StreamToBytes(outputStream);
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
                ThreadHelper.ThrowIfNotOnUIThread();
                IVsGeneratorProgress progress = CodeGeneratorProgress;
                if (progress != null)
                {
                    progress.GeneratorError(warning, level, message, line, column);
                }
            }

            public int Generate(string wszInputFilePath, string bstrInputFileContents, string wszDefaultNamespace,
                                 IntPtr[] pbstrOutputFileContents, out uint pbstrOutputFileContentSize, IVsGeneratorProgress pGenerateProgress)
            {
                // wait for debugger on var
                DebuggerHelper.WaitForDebuggerIfEnabled("NFRESXCODEGEN_DEBUG");

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
                    codeDomProvider = value ?? throw new ArgumentNullException();
                }
            }

            private ServiceProvider SiteServiceProvider
            {
                get
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
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
                ThreadHelper.ThrowIfNotOnUIThread();
                return SiteServiceProvider.GetService(serviceGuid);
            }

            protected object GetService(Type serviceType)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
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
}
