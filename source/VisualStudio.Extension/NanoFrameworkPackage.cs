//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using Microsoft.VisualStudio.ProjectSystem.VS;
using nanoFramework.Tools.VisualStudio.Extension;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Practices.ServiceLocation;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudioTools;
using System.Diagnostics;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.ComponentDiagnostics;

[assembly: ProjectTypeRegistration(projectTypeGuid: NanoFrameworkPackage.ProjectTypeGuid,
                                displayName: "NanoCSharpProject",
                                displayProjectFileExtensions: "#2",
                                defaultProjectExtension: NanoCSharpProjectUnconfigured.ProjectExtension,
                                language: NanoCSharpProjectUnconfigured.Language,
                                resourcePackageGuid: NanoFrameworkPackage.PackageGuid,
                                PossibleProjectExtensions = NanoCSharpProjectUnconfigured.ProjectExtension,
                                Capabilities = NanoCSharpProjectUnconfigured.UniqueCapability
                                )]

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [ProvideAutoLoad(UIContextGuids.NoSolution)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists)]
    [PackageRegistration(AllowsBackgroundLoading = true, RegisterUsing = RegistrationMethod.CodeBase, UseManagedResourcesOnly = true)]
    // info that shown on extension catalog
    [Description("Visual Studio 2017 extension for nanoFramework. Enables creating C# Solutions to be deployed to a target board and provides debugging tools.")]
    // menu for ToolWindow
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // declaration of Device Explorer ToolWindo that (as default) will show tabbed in Solution Explorer
    [ProvideToolWindow(typeof(DeviceExplorer), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    // register nanoDevice communication service
    [ProvideService((typeof(NanoDeviceCommService)), IsAsyncQueryable = true)]
    [Guid(NanoFrameworkPackage.PackageGuid)]
    //[ProvideObject(typeof(CorDebug))]
    [ProvideDebugEngine]
    [ProvidePortSupplier(typeof(DebugPortSupplier), "{D7240956-FE4A-4324-93C9-C56975AF351E}")]
    //[ProvideDebugEngine(AD7Engine.DebugEngineName, typeof(AD7Engine), DebuggerGuids.EngineIdAsString, setNextStatement: true, hitCountBp: true)]
    //[ProvideDebugLanguage("Nano", "{NanoLanguageGuidAsString}", "{47CBD1C6-B83F-488E-A04C-5F58C0F39A73}", DebuggerGuids.EngineIdAsString)]
    //[ProvideDebugPortSupplier("nanoFramework Port", typeof(NanoDebugPortSupplier), NanoDebugPortSupplier.PortSupplierId)] //, typeof(PythonRemoteDebugPortPicker))]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class NanoFrameworkPackage : AsyncPackage, Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget
    {
        /// <summary>
        /// The GUID for this project type
        /// </summary>
        public const string ProjectTypeGuid = "11A8DD76-328B-46DF-9F39-F559912D0360";

        /// <summary>
        /// The GUID for this package.
        /// </summary>
        public const string PackageGuid = "046B40EB-1DE1-4D08-AF61-FDB7592B9BBD";


        public const string NanoCSharpProjectSystemCommandSet = "DF641D51-1E8C-48E4-B549-CC6BCA9BDE19";

        /// <summary>
        /// View model locator 
        /// </summary>
        static internal ViewModelLocator ViewModelLocator;

        /// <summary>
        /// Path for nanoFramework Extension directory
        /// </summary>
        public static string NanoFrameworkExtensionDirectory { get; private set; }

        /// <summary>
        /// Property exposing Visual Studio Output Window Pane 
        /// </summary>
        public static IVsOutputWindowPane WindowPane => (Instance as System.IServiceProvider).GetService(typeof(SVsGeneralOutputWindowPane)) as IVsOutputWindowPane;

        /// <summary>
        /// Property exposing Visual Studio Status bar
        /// </summary>
        public static IVsStatusbar StatusBar => (Instance as System.IServiceProvider).GetService(typeof(SVsStatusbar)) as IVsStatusbar;

        private static NanoFrameworkPackage Instance { get; set; }

        // command set Guid
        public const string _guidNanoDebugPackageCmdSetString = "6A0F19B1-00EF-4215-BD7B-29DEB4425F7C";
        public static readonly Guid _guidNanoDebugPackageCmdSet = new Guid(_guidNanoDebugPackageCmdSetString);

        // command line commands
        public const int _cmdidLaunchNanoDebug = 0x300;

        /// <summary>
        /// Initializes a new instance of the <see cref="NanoFrameworkPackage"/> class.
        /// </summary>
        public NanoFrameworkPackage()
        {
            // Place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.

            // fill the property holding the extension install directory
            var assembly = GetType().Assembly;
            if (assembly.Location == null) throw new Exception("Could not get assembly location!");
            var info = new FileInfo(assembly.Location).Directory;
            if (info == null) throw new Exception("Could not get assembly directory!");
            NanoFrameworkExtensionDirectory = info.FullName;

            Instance = this;
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            AddService(typeof(NanoDeviceCommService), CreateNanoDeviceCommService);

            // Need to add the View model Locator to the application resource dictionary programatically 
            // because at the extension level we don't have 'XAML' access to it
            // try to find if the view model locator is already in the app resources dictionary
            if (System.Windows.Application.Current.TryFindResource("Locator") == null)
            {
                // instantiate the view model locator...
                ViewModelLocator = new ViewModelLocator();

                // ... and add it there
                System.Windows.Application.Current.Resources.Add("Locator", ViewModelLocator);
            }

            ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().Package = this;

            // need to switch to the main thread to initialize the command handlers
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DeviceExplorerCommand.Initialize(this, ViewModelLocator, await this.GetServiceAsync(typeof(NanoDeviceCommService)) as INanoDeviceCommService);
            DeployProvider.Initialize(this, ViewModelLocator);

            //await CreateCommandHandlersAsync();

            await TaskScheduler.Default;

            ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().NanoDeviceCommService = await this.GetServiceAsync(typeof(NanoDeviceCommService)) as INanoDeviceCommService;

            await base.InitializeAsync(cancellationToken, progress);
        }

        public async Task<object> CreateNanoDeviceCommService(IAsyncServiceContainer container, CancellationToken cancellationToken, Type serviceType)
        {
            NanoDeviceCommService service = null;

            await System.Threading.Tasks.Task.Run(() => {
                service = new NanoDeviceCommService(this);
            });

            return service;
        }

        private int LaunchNanoDebug(uint nCmdExecOpt, IntPtr pvaIn, IntPtr pvaOut)
        {
            int hr;
            string executable = string.Empty;
            bool checkExecutableExists = false;
            string options = string.Empty;

            //if (IsQueryParameterList(pvaIn, pvaOut, nCmdExecOpt))
            //{
            //    Marshal.GetNativeVariantForObject("$ /switchdefs:\"" + LaunchMIDebugCommandSyntax + "\"", pvaOut);
            //    return VSConstants.S_OK;
            //}

            //string arguments;
            //hr = EnsureString(pvaIn, out arguments);
            //if (hr != VSConstants.S_OK)
            //    return hr;

            //IVsParseCommandLine parseCommandLine = (IVsParseCommandLine)GetService(typeof(SVsParseCommandLine));
            //hr = parseCommandLine.ParseCommandTail(arguments, iMaxParams: -1);
            //if (ErrorHandler.Failed(hr))
            //    return hr;

            //hr = parseCommandLine.HasParams();
            //if (ErrorHandler.Failed(hr))
            //    return hr;
            //if (hr == VSConstants.S_OK || parseCommandLine.HasSwitches() != VSConstants.S_OK)
            //{
            //    string message = string.Concat("Unexpected syntax for MIDebugLaunch command. Expected:\n",
            //        "Debug.MIDebugLaunch /Executable:<path_or_logical_name> /OptionsFile:<path>");
            //    throw new ApplicationException(message);
            //}

            //hr = parseCommandLine.EvaluateSwitches(LaunchMIDebugCommandSyntax);
            //if (ErrorHandler.Failed(hr))
            //    return hr;

            //if (parseCommandLine.GetSwitchValue((int)LaunchMIDebugCommandSwitchEnum.Executable, out executable) != VSConstants.S_OK ||
            //    string.IsNullOrWhiteSpace(executable))
            //{
            //    throw new ArgumentException("Executable must be specified");
            //}

            //string optionsFilePath;
            //if (parseCommandLine.GetSwitchValue((int)LaunchMIDebugCommandSwitchEnum.OptionsFile, out optionsFilePath) == 0)
            //{
            //    // When using the options file, we want to allow the executable to be just a logical name, but if
            //    // one enters a real path, we should make sure it isn't mistyped. If the path contains a slash, we assume it 
            //    // is meant to be a real path so enforce that it exists
            //    checkExecutableExists = (executable.IndexOf('\\') >= 0);

            //    if (string.IsNullOrWhiteSpace(optionsFilePath))
            //        throw new ArgumentException("Value expected for '/OptionsFile' option");

            //    if (!File.Exists(optionsFilePath))
            //        throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Options file '{0}' does not exist", optionsFilePath));

            //    options = File.ReadAllText(optionsFilePath);
            //}

            //if (checkExecutableExists)
            //{
            //    if (!File.Exists(executable))
            //    {
            //        throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Executable '{0}' does not exist", executable));
            //    }

            //    executable = Path.GetFullPath(executable);
            //}

            LaunchDebugTarget(executable, options);

            return 0;
        }

        private void LaunchDebugTarget(string filePath, string options)
        {
            IVsDebugger4 debugger = (IVsDebugger4)GetService(typeof(IVsDebugger));
            VsDebugTargetInfo4[] debugTargets = new VsDebugTargetInfo4[1];
            debugTargets[0].dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
            debugTargets[0].bstrExe = filePath;
            debugTargets[0].bstrOptions = options;
            debugTargets[0].guidLaunchDebugEngine = CorDebug.EngineGuid; //DebuggerGuids.EngineId;
            VsDebugTargetProcessInfo[] processInfo = new VsDebugTargetProcessInfo[debugTargets.Length];

            debugger.LaunchDebugTargets4(1, debugTargets, processInfo);
        }

        #region IOleCommandTarget Members

        int Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget.Exec(ref Guid cmdGroup, uint nCmdID, uint nCmdExecOpt, IntPtr pvaIn, IntPtr pvaOut)
        {
            Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget oleCommandTarget = (Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)this.GetService(typeof(Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget));

            if (oleCommandTarget != null)
            {
                if (cmdGroup == _guidNanoDebugPackageCmdSet)
                {
                    switch (nCmdID)
                    {
                        case _cmdidLaunchNanoDebug:
                            return LaunchNanoDebug(nCmdExecOpt, pvaIn, pvaOut);

                        default:
                            Debug.Fail("Unknown command id");
                            return Microsoft.VisualStudio.VSConstants.E_NOTIMPL;
                    }
                }

                return oleCommandTarget.Exec(cmdGroup, nCmdID, nCmdExecOpt, pvaIn, pvaOut);
            }

            return -2147221248;
        }

        int Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget.QueryStatus(ref Guid cmdGroup, uint cCmds, Microsoft.VisualStudio.OLE.Interop.OLECMD[] prgCmds, IntPtr pCmdText)
        {
            Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget oleCommandTarget = (Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)this.GetService(typeof(Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget));

            if (oleCommandTarget != null)
            {
                if (cmdGroup == _guidNanoDebugPackageCmdSet)
                {
                    switch (prgCmds[0].cmdID)
                    {
                        case _cmdidLaunchNanoDebug:
                            prgCmds[0].cmdf |= (uint)(Microsoft.VisualStudio.OLE.Interop.OLECMDF.OLECMDF_SUPPORTED | Microsoft.VisualStudio.OLE.Interop.OLECMDF.OLECMDF_ENABLED | Microsoft.VisualStudio.OLE.Interop.OLECMDF.OLECMDF_INVISIBLE);
                            return Microsoft.VisualStudio.VSConstants.S_OK;

                        default:
                            Debug.Fail("Unknown command id");
                            return Microsoft.VisualStudio.VSConstants.E_NOTIMPL;
                    }
                }

                return oleCommandTarget.QueryStatus(ref cmdGroup, cCmds, prgCmds, pCmdText);
            }

            return -2147221248;
        }

        #endregion

    }
}
