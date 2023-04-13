//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft;
using Microsoft.VisualStudio.ProjectSystem.VS;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextTemplating.VSHost;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.VisualStudio.Extension;
using nanoFramework.Tools.VisualStudio.Extension.AutomaticUpdates;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

[assembly: ProjectTypeRegistration(projectTypeGuid: NanoFrameworkPackage.ProjectTypeGuid,
                                displayName: "NanoCSharpProject",
                                displayProjectFileExtensions: ".NET nanoFramework Project Files (*.nfproj);*.nfproj",
                                defaultProjectExtension: NanoCSharpProjectUnconfigured.ProjectExtension,
                                language: NanoCSharpProjectUnconfigured.Language,
                                resourcePackageGuid: NanoFrameworkPackage.PackageGuidString,
                                PossibleProjectExtensions = NanoCSharpProjectUnconfigured.ProjectExtension,
                                Capabilities = NanoCSharpProjectUnconfigured.UniqueCapability
                                )]
[assembly: ProvideCodeBase(CodeBase = "$PackageFolder$\\nanoFramework.Tools.VS2022.Extension.dll")]
[assembly: ProvideCodeBase(CodeBase = "$PackageFolder$\\CommunityToolkit.Mvvm.dll")]
namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(AllowsBackgroundLoading = true, RegisterUsing = RegistrationMethod.CodeBase, UseManagedResourcesOnly = true)]
    // info that shown on extension catalog
    [Description("Visual Studio 2022 extension for .NET nanoFramework. Enables creating C# Solutions to be deployed to a target board and provides debugging tools.")]
    // menu for ToolWindow
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // declaration of Device Explorer ToolWindow that (as default) will show tabbed in Solution Explorer Windows (GUID below)
    [ProvideToolWindow(typeof(DeviceExplorer), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    // register nanoDevice communication service
    [ProvideService((typeof(NanoDeviceCommService)), IsAsyncQueryable = true)]
    [Guid(PackageGuidString)]
    [ProvideObject(typeof(CorDebug))]
    [ProvideDebugEngine("Managed", typeof(CorDebug), CorDebug.EngineId, setNextStatement: true, hitCountBp: true)]
    [ProvideDebugPortSupplier(".NET nanoFramework Port Supplier", typeof(DebugPortSupplier), DebugPortSupplier.PortSupplierId)]
    // register code generator for resources
    [ProvideObject(typeof(nFResXFileCodeGenerator))]
    [ProvideCodeGenerator(typeof(nFResXFileCodeGenerator), nFResXFileCodeGenerator.Name, nFResXFileCodeGenerator.Description, true, ProjectSystem = ProvideCodeGeneratorAttribute.CSharpProjectGuid)]
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
        ////////////////////////////////////////////////////////
        // this GUID is coming from NanoFrameworkPackage.vsct //
        ////////////////////////////////////////////////////////
        public const string PackageGuidString = "1b4aea27-9d6e-46a4-9868-f2d9a052c821";

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
        private const string SETTINGS_ALLOW_PREVIEW_IMAGES_KEY = "IncludePrereleaseUpdates";
        private const string SETTINGS_VIRTUAL_DEVICE_ENABLE_KEY = "VirtualDeviceEnable";
        private const string SETTINGS_VIRTUAL_DEVICE_AUTO_UPDATE_NANOCLR_KEY = "VirtualDeviceAutoUpdateNanoCLR";
        private const string SETTINGS_VIRTUAL_DEVICE_PORT = "VirtualDevicePort";

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
                    s_SettingAutoUpdateEnable = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_AUTO_UPDATE_ENABLE_KEY, "False"));
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

        private static bool? s_SettingIncludePrereleaseUpdates = null;

        /// <summary>
        /// Setting to enable updates with preview images.
        /// The value is persisted per user.
        /// Default is <see langword="true"/>.
        /// </summary>
        public static bool SettingIncludePrereleaseUpdates
        {
            get
            {
                if (!s_SettingIncludePrereleaseUpdates.HasValue)
                {
                    s_SettingIncludePrereleaseUpdates = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_ALLOW_PREVIEW_IMAGES_KEY, "True"));
                }

                return s_SettingIncludePrereleaseUpdates.Value;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_ALLOW_PREVIEW_IMAGES_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingIncludePrereleaseUpdates = value;
            }
        }
      
        private static bool? s_SettingVirtualDeviceAutoUpdateNanoClrImage = null;
        /// <summary>
        /// Setting to enable auto updating nanoCLR image for the virtual device.
        /// The value is persisted per user
        /// Default is <see langword="false"/>
        /// </summary>
        public static bool SettingVirtualDeviceAutoUpdateNanoClrImage
        {
            get
            {
                if (!s_SettingVirtualDeviceAutoUpdateNanoClrImage.HasValue)
                {
                    s_SettingVirtualDeviceAutoUpdateNanoClrImage = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_VIRTUAL_DEVICE_AUTO_UPDATE_NANOCLR_KEY, "True"));
                }

                return s_SettingVirtualDeviceAutoUpdateNanoClrImage.Value;

            }
            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_VIRTUAL_DEVICE_AUTO_UPDATE_NANOCLR_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingVirtualDeviceAutoUpdateNanoClrImage = value;

            }
        }

        private static bool? s_SettingVirtualDeviceEnable = null;
        /// <summary>
        /// Setting to enable the use of a virtual device running on the development machine as a command line interface (CLI)
        /// The value is persisted per user
        /// Default is <see langword="false"/>
        /// </summary>
        public static bool SettingVirtualDeviceEnable
        {
            get
            {
                if (!s_SettingVirtualDeviceEnable.HasValue)
                {
                    s_SettingVirtualDeviceEnable = bool.Parse((string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_VIRTUAL_DEVICE_ENABLE_KEY, "False"));
                }

                return s_SettingVirtualDeviceEnable.Value;

            }
            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_VIRTUAL_DEVICE_ENABLE_KEY, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingVirtualDeviceEnable = value;

            }
        }

        private static string s_SettingVirtualDevicePort = null;
        private static INanoDeviceCommService _nanoDeviceCommService;
        private static IVirtualDeviceService _virtualDeviceService;

        /// <summary>
        /// Setting to store COM port for the virtual device
        /// The value is persisted per user.
        /// Default is empty.
        /// </summary>
        public static string SettingVirtualDevicePort
        {
            get
            {
                if (string.IsNullOrEmpty(s_SettingVirtualDevicePort))
                {
                    s_SettingVirtualDevicePort = (string)s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY).GetValue(SETTINGS_VIRTUAL_DEVICE_PORT, string.Empty);
                }

                return s_SettingVirtualDevicePort;
            }

            set
            {
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).SetValue(SETTINGS_VIRTUAL_DEVICE_PORT, value);
                s_instance.UserRegistryRoot.OpenSubKey(EXTENSION_SUBKEY, true).Flush();

                s_SettingVirtualDevicePort = value;
            }
        }

        #endregion

        /// <summary>
        /// Provides direct access to <see cref="INanoDeviceCommService"/> service.
        /// To be used by providers and other classes in the package.
        /// </summary>
        public static INanoDeviceCommService NanoDeviceCommService 
        {
            get
            {
                if (_nanoDeviceCommService is null)
                {
                    _nanoDeviceCommService = ThreadHelper.JoinableTaskFactory.Run(async delegate
                    {
                        return await s_instance.GetServiceAsync(typeof(NanoDeviceCommService)) as INanoDeviceCommService;
                    });
                }

                return _nanoDeviceCommService;
            }
        }

        /// <summary>
        /// Provides direct access to <see cref="IVirtualDeviceService"/> service.
        /// To be used by providers and other classes in the package.
        /// </summary>
        internal static IVirtualDeviceService VirtualDeviceService 
        {
            get
            {
                if (_virtualDeviceService is null)
                {
                    _virtualDeviceService = ThreadHelper.JoinableTaskFactory.Run(async delegate
                    {
                        return await s_instance.GetServiceAsync(typeof(VirtualDeviceService)) as IVirtualDeviceService;
                    });
                }

                return _virtualDeviceService;
            }
        }

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
            // fill the property holding the extension install directory
            var assembly = GetType().Assembly;
            if (assembly.Location == null)
            {
                throw new FileNotFoundException("Could not find location of VS extension assembly!");
            }

            var info = new FileInfo(assembly.Location).Directory;
            if (info == null)
            {
                throw new FileNotFoundException("Could not find directory location of VS extension assembly!");
            }

            NanoFrameworkExtensionDirectory = info.FullName;

            // fill the property of the DLL version
            NanoFrameworkExtensionVersion = new AssemblyName(assembly.FullName).Version;

            s_instance = this;
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // make sure "our" key exists and it's writable
            s_instance.UserRegistryRoot.CreateSubKey(EXTENSION_SUBKEY, true);
 
            AddService(typeof(NanoDeviceCommService), CreateNanoDeviceCommServiceAsync);
            AddService(typeof(VirtualDeviceService), CreateVirtualDeviceManagerServiceAsync);

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

            Ioc.Default.GetRequiredService<DeviceExplorerViewModel>().Package = this;

            await MessageCentre.InitializeAsync(this, ".NET nanoFramework Extension");

            DeployProvider.Initialize(this, viewModelLocator);
            UpdateManager.Initialize(this, viewModelLocator);

            // Enable debugger UI context
            UIContext.FromUIContextGuid(CorDebug.EngineGuid).IsActive = true;

            await DeviceExplorerCommand.InitializeAsync(this, viewModelLocator);
            VirtualDeviceService.InitVirtualDeviceAsync().FireAndForget(); ;
        }

        #endregion


        public async Task<object> CreateNanoDeviceCommServiceAsync(IAsyncServiceContainer container, CancellationToken cancellationToken, Type serviceType)
        {
            NanoDeviceCommService service = null;

            await System.Threading.Tasks.Task.Run(() =>
            {
                service = new NanoDeviceCommService(this);
            });

            return service;
        }

        public async Task<object> CreateVirtualDeviceManagerServiceAsync(IAsyncServiceContainer container, CancellationToken cancellationToken, Type serviceType)
        {
            VirtualDeviceService service = null;

            await System.Threading.Tasks.Task.Run(() =>
            {
                service = new VirtualDeviceService(this);
            });

            return service;
        }

        private int LaunchNanoDebug(uint nCmdExecOpt, IntPtr pvaIn, IntPtr pvaOut)
        {
            string executable = string.Empty;
            string options = string.Empty;

            _ = LaunchDebugTargetAsync(executable, options);

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

        protected override int QueryClose(out bool canClose)
        {
            var res = base.QueryClose(out canClose);

            if (canClose)
            {
                VirtualDeviceService?.StopVirtualDevice(true);
            }

            return res;
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
