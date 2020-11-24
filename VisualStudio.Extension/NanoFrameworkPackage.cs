//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//
using GalaSoft.MvvmLight.Ioc;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem.VS;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextTemplating.VSHost;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.VisualStudio.Extension;
using nanoFramework.Tools.VisualStudio.Extension.FirmwareUpdate;
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
using System.Windows;

[assembly: ProjectTypeRegistration(projectTypeGuid: NanoFrameworkPackage.ProjectTypeGuid,
                                displayName: "NanoCSharpProject",
                                displayProjectFileExtensions: "nanoFramework Project Files (*.nfproj);*.nfproj",
                                defaultProjectExtension: NanoCSharpProjectUnconfigured.ProjectExtension,
                                language: NanoCSharpProjectUnconfigured.Language,
                                resourcePackageGuid: NanoFrameworkPackage.PackageGuid,
                                PossibleProjectExtensions = NanoCSharpProjectUnconfigured.ProjectExtension,
                                Capabilities = NanoCSharpProjectUnconfigured.UniqueCapability
                                )]

namespace nanoFramework.Tools.VisualStudio.Extension
{
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
        private const string SETTINGS_GENERATE_DEPLOYMENT_IMAGE_KEY = "GenerateDeploymentImage";
        private const string SETTINGS_INCLUDE_CONFIG_BLOCK_IN_DEPLOYMENT_IMAGE_KEY = "IncludeConfigBlockInDeploymentImage";
        private const string SETTINGS_PATH_OF_FLASH_DUMP_CACHE_IMAGE_KEY = "PathOfFlashDumpCache";
        private const string SETTINGS_PORT_BLACK_LIST_KEY = "PortBlackList";
        private const string SETTINGS_AUTO_UPDATE_ENABLE_KEY = "AutoUpdateEnable";
        private const string SETTINGS_ALLOW_PREVIEW_IMAGES_KEY = "AllowPreviewUpdates";

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
                try
                {
                    if (!s_OptionShowInternalErrors.HasValue)
                    {
                        s_OptionShowInternalErrors = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SHOW_INTERNAL_ERRORS_KEY, "False"));
                    }

                    return s_OptionShowInternalErrors.Value;
                }
                catch
                {
                    // default to false
                    return false;
                }
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

        private static bool? s_SettingGenerateDeploymentImage;
        /// <summary>
        /// Setting to generate deployment image on deploy.
        /// The value is persisted per user.
        /// Default is false.
        /// </summary>
        public static bool SettingGenerateDeploymentImage
        {
            get
            {
                if (!s_SettingGenerateDeploymentImage.HasValue)
                {
                    s_SettingGenerateDeploymentImage = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_GENERATE_DEPLOYMENT_IMAGE_KEY, "False"));
                }

                return s_SettingGenerateDeploymentImage.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_GENERATE_DEPLOYMENT_IMAGE_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingGenerateDeploymentImage = value;
            }
        }

        private static bool? s_SettingIncludeConfigBlockInDeploymentImage;
        /// <summary>
        /// Setting to include configuration block when generating deployment image.
        /// The value is persisted per user.
        /// Default is false.
        /// </summary>
        public static bool SettingIncludeConfigBlockInDeploymentImage
        {
            get
            {
                if (!s_SettingIncludeConfigBlockInDeploymentImage.HasValue)
                {
                    s_SettingIncludeConfigBlockInDeploymentImage = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_INCLUDE_CONFIG_BLOCK_IN_DEPLOYMENT_IMAGE_KEY, "False"));
                }

                return s_SettingIncludeConfigBlockInDeploymentImage.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_INCLUDE_CONFIG_BLOCK_IN_DEPLOYMENT_IMAGE_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingIncludeConfigBlockInDeploymentImage = value;
            }
        }

        private static string s_SettingPathOfFlashDumpCache = null;
        /// <summary>
        /// Setting to store path where to cache flash dump. If empty the cache will be the project output path.
        /// The value is persisted per user.
        /// Default is empty.
        /// </summary>
        public static string SettingPathOfFlashDumpCache
        {
            get
            {
                if (string.IsNullOrEmpty(s_SettingPathOfFlashDumpCache))
                {
                    s_SettingPathOfFlashDumpCache = (string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_PATH_OF_FLASH_DUMP_CACHE_IMAGE_KEY);
                }

                return s_SettingPathOfFlashDumpCache;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_PATH_OF_FLASH_DUMP_CACHE_IMAGE_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingPathOfFlashDumpCache = value;
            }
        }

        private static string s_SettingPortBlackList = null;
        /// <summary>
        /// Setting to store COM port black list.
        /// The value is persisted per user.
        /// Default is empty.
        /// </summary>
        public static string SettingPortBlackList
        {
            get
            {
                if (string.IsNullOrEmpty(s_SettingPortBlackList))
                {
                    s_SettingPortBlackList = (string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_PORT_BLACK_LIST_KEY);
                }

                return s_SettingPortBlackList;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_PORT_BLACK_LIST_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingPortBlackList = value;
            }
        }

        private static bool? s_SettingAutoUpdateEnable = null;

        /// <summary>
        /// Setting to enable automatic update of target images.
        /// The value is persisted per user.
        /// Default is <see langword="true"/>.
        /// </summary>
        public static bool SettingAutoUpdateEnable
        {
            get
            {
                if (!s_SettingAutoUpdateEnable.HasValue)
                {
                    s_SettingAutoUpdateEnable = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_AUTO_UPDATE_ENABLE_KEY, "True"));
                }

                return s_SettingAutoUpdateEnable.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_AUTO_UPDATE_ENABLE_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingAutoUpdateEnable = value;
            }
        }

        private static bool? s_SettingAllowPreviewUpdates = null;

        /// <summary>
        /// Setting to enable updates with preview images.
        /// The value is persisted per user.
        /// Default is <see langword="true"/>.
        /// </summary>
        public static bool SettingAllowPreviewUpdates
        {
            get
            {
                if (!s_SettingAllowPreviewUpdates.HasValue)
                {
                    s_SettingAllowPreviewUpdates = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_ALLOW_PREVIEW_IMAGES_KEY, "True"));
                }

                return s_SettingAllowPreviewUpdates.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_ALLOW_PREVIEW_IMAGES_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingAllowPreviewUpdates = value;
            }
        }

        #endregion

        /// <summary>
        /// Provides direct access to <see cref="INanoDeviceCommService"/> service.
        /// To be used by providers and other classes in the package.
        /// </summary>
        public static INanoDeviceCommService NanoDeviceCommService { get; private set; }

        private static NanoFrameworkPackage s_instance { get; set; }

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

#pragma warning disable S112 // OK to throw exception here
            if (assembly.Location == null) throw new Exception("Could not get assembly location!");
            var info = new FileInfo(assembly.Location).Directory;
            if (info == null) throw new Exception("Could not get assembly directory!");
#pragma warning restore S112 // General exceptions should never be thrown

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
            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                // check Windows version
                if (Environment.OSVersion.Version.Major < 10)
                {
                    // the extension won't run properly if we are not on Windows 10, warn user
                    MessageBox.Show("nanoFramework Extension requires Windows 10!", "nanoFramework extension", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // make sure "our" key exists and it's writeable
                    s_instance.UserRegistryRoot.CreateSubKey(EXTENSION_SUBKEY, true);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    AddService(typeof(NanoDeviceCommService), CreateNanoDeviceCommServiceAsync);

                    NanoDeviceCommService = await GetServiceAsync(typeof(NanoDeviceCommService)) as INanoDeviceCommService;

                    ViewModelLocator viewModelLocator = null;

                    // Need to add the View model Locator to the application resource dictionary programmatically 
                    // because at the extension level we don't have 'XAML' access to it
                    // try to find if the view model locator is already in the app resources dictionary
                    if (Application.Current.TryFindResource("Locator") == null)
                    {
                        // instantiate the view model locator...
                        viewModelLocator = new ViewModelLocator();

                        // ... and add it there
                        Application.Current.Resources.Add("Locator", viewModelLocator);
                    }

                    SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().Package = this;

                    await MessageCentre.InitializeAsync(this, "nanoFramework Extension");

                    await DeviceExplorerCommand.InitializeAsync(this, viewModelLocator);
                    DeployProvider.Initialize(this, viewModelLocator);
                    UpdateManager.Initialize(this, viewModelLocator);

                    // Enable debugger UI context
                    UIContext.FromUIContextGuid(CorDebug.EngineGuid).IsActive = true;

                    OutputWelcomeMessage();
                }
            });

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
            System.Threading.Tasks.Task.Run(async () =>
            {
                // schedule this to wait a few seconds (allowing VS to load) before doing it's thing
                await System.Threading.Tasks.Task.Delay(5000);

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
                if (OptionDisableDeviceWatchers)
                {
                    MessageCentre.OutputMessage(Environment.NewLine);
                    MessageCentre.OutputMessage("*******************************************************************************");
                    MessageCentre.OutputMessage("** Device Watchers are DISABLED. Won't be able to connect to any nanoDevice. **");
                    MessageCentre.OutputMessage("*******************************************************************************");
                    MessageCentre.OutputMessage(Environment.NewLine);
                }
            }).WaitWithoutInlining();
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
            var debugger = await GetServiceAsync(typeof(IVsDebugger)) as IVsDebugger4;
            Assumes.Present(debugger);

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
