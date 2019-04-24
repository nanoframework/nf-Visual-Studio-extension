//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using GalaSoft.MvvmLight.Ioc;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem.VS;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextTemplating.VSHost;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.VisualStudio.Extension;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [PackageRegistration(AllowsBackgroundLoading = true, RegisterUsing = RegistrationMethod.CodeBase, UseManagedResourcesOnly = true)]
    // info that shown on extension catalog
    [Description("Visual Studio 2017 extension for nanoFramework. Enables creating C# Solutions to be deployed to a target board and provides debugging tools.")]
    // menu for ToolWindow
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // declaration of Device Explorer ToolWindow that (as default) will show tabbed in Solution Explorer
    [ProvideToolWindow(typeof(DeviceExplorer), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    // register nanoDevice communication service
    [ProvideService((typeof(NanoDeviceCommService)), IsAsyncQueryable = true)]
    [Guid(NanoFrameworkPackage.PackageGuid)]
    [ProvideObject(typeof(CorDebug))]
    [ProvideDebugEngine("Managed", typeof(CorDebug), CorDebug.EngineId, setNextStatement: true, hitCountBp: true)]
    [ProvideDebugPortSupplier("nanoFramework Port Supplier", typeof(DebugPortSupplier), DebugPortSupplier.PortSupplierId)]
    // register code generator for resources
    [ProvideObject(typeof(nFResXFileCodeGenerator))]
    [ProvideCodeGenerator(typeof(nFResXFileCodeGenerator), nFResXFileCodeGenerator.Name, nFResXFileCodeGenerator.Description, true, ProjectSystem = ProvideCodeGeneratorAttribute.CSharpProjectGuid)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class NanoFrameworkPackage : AsyncPackage, Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget
    {
        /// <summary>
        /// The GUID for this project type
        /// </summary>
        public const string ProjectTypeGuid = "11A8DD76-328B-46DF-9F39-F559912D0360";
        private const string ProjectTypeGuidFormatted = "{" + ProjectTypeGuid + "}";

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
        /// Version of the nanoFramework Extension DLL
        /// </summary>
        public static Version NanoFrameworkExtensionVersion { get; private set; }


        #region user options related stuff

        private const string EXTENSION_SUBKEY = "nanoFrameworkExtension";

        private const string SHOW_INTERNAL_ERRORS_KEY = "ShowInternalErrors";
        private const string DISABLE_DEVICE_WATCHERS_KEY = "DisableDeviceWatchers";

        private static bool? s_OptionShowInternalErrors;
        /// <summary>
        /// User option for outputting internal extension errors to the extension output pane.
        /// The value is persisted per user.
        /// Default is false.
        /// </summary>
        public static bool OptionShowInternalErrors
        {
            get
            {
                if (!s_OptionShowInternalErrors.HasValue)
                {
                    s_OptionShowInternalErrors = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SHOW_INTERNAL_ERRORS_KEY, "False"));
                }

                return s_OptionShowInternalErrors.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SHOW_INTERNAL_ERRORS_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_OptionShowInternalErrors = value;
            }
        }


        private static bool? s_OptionDisableDeviceWatchers;
        /// <summary>
        /// User option for outputting internal extension errors to the extension output pane.
        /// The value is persisted per user.
        /// Default is false.
        /// </summary>
        public static bool OptionDisableDeviceWatchers
        {
            get
            {
                if (!s_OptionDisableDeviceWatchers.HasValue)
                {
                    s_OptionDisableDeviceWatchers = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(DISABLE_DEVICE_WATCHERS_KEY, "False"));
                }

                return s_OptionDisableDeviceWatchers.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(DISABLE_DEVICE_WATCHERS_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_OptionDisableDeviceWatchers = value;
            }
        }

        #endregion

        public static INanoDeviceCommService NanoDeviceCommService
        {
            get
            {
                if (s_nanoDeviceCommService == null)
                {
                    s_nanoDeviceCommService = s_instance.GetService(typeof(NanoDeviceCommService)) as INanoDeviceCommService;
                }

                return s_nanoDeviceCommService;
            }
        }

        private static NanoFrameworkPackage s_instance { get; set; }

        private static INanoDeviceCommService s_nanoDeviceCommService;

        // command set Guid
        public const string _guidNanoDebugPackageCmdSetString = "6A0F19B1-00EF-4215-BD7B-29DEB4425F7C";
        public static readonly Guid s_guidNanoDebugPackageCmdSet = new Guid(_guidNanoDebugPackageCmdSetString);

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

            // fill the property of the DLL version
            NanoFrameworkExtensionVersion = new AssemblyName(assembly.FullName).Version;

            s_instance = this;
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // make sure "our" key exists and it's writeable
            s_instance.UserRegistryRoot.CreateSubKey(EXTENSION_SUBKEY, true);

            AddService(typeof(NanoDeviceCommService), CreateNanoDeviceCommServiceAsync);

            // Need to add the View model Locator to the application resource dictionary programmatically 
            // because at the extension level we don't have 'XAML' access to it
            // try to find if the view model locator is already in the app resources dictionary
            if (System.Windows.Application.Current.TryFindResource("Locator") == null)
            {
                // instantiate the view model locator...
                ViewModelLocator = new ViewModelLocator();

                // ... and add it there
                System.Windows.Application.Current.Resources.Add("Locator", ViewModelLocator);
            }

            SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().Package = this;

            await MessageCentre.InitializeAsync(this, "nanoFramework Extension");

            await DeviceExplorerCommand.InitializeAsync(this, ViewModelLocator);
            DeployProvider.Initialize(this, ViewModelLocator);

            // Enable debugger UI context
            UIContext.FromUIContextGuid(CorDebug.EngineGuid).IsActive = true;

            OutputWelcomeMessage();

            await base.InitializeAsync(cancellationToken, progress);
        }

        public async Task<object> CreateNanoDeviceCommServiceAsync(IAsyncServiceContainer container, CancellationToken cancellationToken, Type serviceType)
        {
            NanoDeviceCommService service = null;

            await System.Threading.Tasks.Task.Run(() => {
                service = new NanoDeviceCommService(this);
            });

            return service;
        }

        private void OutputWelcomeMessage()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                // schedule this to wait for a few seconds before doing it's thing allow VS to load
                System.Threading.Tasks.Task.Delay(5000);

                // loaded 
                MessageCentre.OutputMessage($"** nanoFramework extension v{NanoFrameworkExtensionVersion.ToString()} loaded **");

                // intro messages
                MessageCentre.OutputMessage("GitHub repo: https://github.com/nanoframework/Home");
                MessageCentre.OutputMessage("Report issues: https://github.com/nanoframework/Home/issues");
                MessageCentre.OutputMessage("Join our Discord community: https://discord.gg/gCyBu8T");
                MessageCentre.OutputMessage("Join our Hackster.io platform: https://www.hackster.io/nanoframework");
                MessageCentre.OutputMessage("Follow us on Twitter: https://twitter.com/nanoframework");
                MessageCentre.OutputMessage("Follow our YouTube channel: https://www.youtube.com/c/nanoFramework");
                MessageCentre.OutputMessage("Star our GitHub repos: https://github.com/nanoframework/Home");
                MessageCentre.OutputMessage("Add a short review or rate the VS extension: https://marketplace.visualstudio.com/items?itemName=vs-publisher-1470366.nanoFrameworkVS2017Extension");
                MessageCentre.OutputMessage(Environment.NewLine);

                // check Windows version
                if (Environment.OSVersion.Version < new Version(6, 2, 9200, 0))
                {
                    // this is running on a Windows version lower than Windows 10
                    MessageCentre.OutputMessage(Environment.NewLine);
                    MessageCentre.OutputMessage("*************************************************************************");
                    MessageCentre.OutputMessage("** Seems that you are running this on a Window version earlier than 10 **");
                    MessageCentre.OutputMessage("** nanoFramework debug engine component requires Windows 10            **");
                    MessageCentre.OutputMessage("*************************************************************************");
                    MessageCentre.OutputMessage(Environment.NewLine);
                }

                // check device watchers option
                if(OptionDisableDeviceWatchers)
                {
                    MessageCentre.OutputMessage(Environment.NewLine);
                    MessageCentre.OutputMessage("*******************************************************************************");
                    MessageCentre.OutputMessage("** Device Watchers are DISABLED. Won't be able to connect to any nanoDevice. **");
                    MessageCentre.OutputMessage("*******************************************************************************");
                    MessageCentre.OutputMessage(Environment.NewLine);
                }
            });
        }

        private int LaunchNanoDebug(uint nCmdExecOpt, IntPtr pvaIn, IntPtr pvaOut)
        {
            int hr;
            string executable = string.Empty;
            string options = string.Empty;

            var dummyWait = LaunchDebugTargetAsync(executable, options);

            return 0;
        }

        private async System.Threading.Tasks.Task LaunchDebugTargetAsync(string filePath, string options)
        {
            ////////////////////////////////////////////////////////
            //  EXPERIMENTAL to launch debug from VS command line //
            ////////////////////////////////////////////////////////
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            IVsDebugger4 debugger = (IVsDebugger4)GetServiceAsync(typeof(IVsDebugger));
            VsDebugTargetInfo4[] debugTargets = new VsDebugTargetInfo4[1];
            debugTargets[0].dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
            debugTargets[0].bstrExe = typeof(CorDebugProcess).Assembly.Location;
            debugTargets[0].bstrArg = options;
            debugTargets[0].guidPortSupplier = DebugPortSupplier.PortSupplierGuid;
            debugTargets[0].guidLaunchDebugEngine = CorDebug.EngineGuid;
            debugTargets[0].bstrCurDir = filePath;
            VsDebugTargetProcessInfo[] processInfo = new VsDebugTargetProcessInfo[debugTargets.Length];

            debugger.LaunchDebugTargets4(1, debugTargets, processInfo);
        }

        #region IOleCommandTarget Members

        int Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget.Exec(ref Guid cmdGroup, uint nCmdID, uint nCmdExecOpt, IntPtr pvaIn, IntPtr pvaOut)
        {
            Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget oleCommandTarget = (Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)GetService(typeof(Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget));

            if (oleCommandTarget != null)
            {
                if (cmdGroup == s_guidNanoDebugPackageCmdSet)
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
            Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget oleCommandTarget = (Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)GetService(typeof(Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget));

            if (oleCommandTarget != null)
            {
                if (cmdGroup == s_guidNanoDebugPackageCmdSet)
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
