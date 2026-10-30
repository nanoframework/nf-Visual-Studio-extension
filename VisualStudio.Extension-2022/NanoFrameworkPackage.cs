//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.ProjectSystem.VS;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextTemplating.VSHost;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.VisualStudio.Extension;
using nanoFramework.Tools.VisualStudio.Extension.AutomaticUpdates;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
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
    // the telemetry dependencies (OpenTelemetry, Azure Monitor exporter) are newer than the ones Visual Studio ships, load them from the extension folder
    [ProvideBindingPath]
    // info that shown on extension catalog
    [Description("Visual Studio 2022 extension for .NET nanoFramework. Enables creating C# Solutions to be deployed to a target board and provides debugging tools.")]
    // commands for the command line
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    [ProvideObject(typeof(CorDebug))]
    [ProvideDebugEngine("Managed", typeof(CorDebug), CorDebug.EngineId, setNextStatement: true, hitCountBp: true)]
    [ProvideDebugPortSupplier(".NET nanoFramework Port Supplier", typeof(DebugPortSupplier), DebugPortSupplier.PortSupplierId)]
    // register code generator for resources
    [ProvideObject(typeof(nFResXFileCodeGenerator))]
    [ProvideService(typeof(NanoDeviceCommService), IsAsyncQueryable = true)]
    [ProvideService(typeof(VirtualDeviceService), IsAsyncQueryable = true)]
    [ProvideCodeGenerator(typeof(nFResXFileCodeGenerator), nFResXFileCodeGenerator.Name, nFResXFileCodeGenerator.Description, true, ProjectSystem = ProvideCodeGeneratorAttribute.CSharpProjectGuid)]
    // the names of the options category and pages are in VSPackage.resx (200-204)
    [ProvideOptionPage(typeof(NanoOptionsPageGeneral), OptionsCategoryName, "General", 200, 201, true, Sort = 0)]
    [ProvideOptionPage(typeof(NanoOptionsPageUpdates), OptionsCategoryName, "Updates", 200, 202, true, Sort = 1)]
    [ProvideOptionPage(typeof(NanoOptionsPageVirtualDevice), OptionsCategoryName, "Virtual Device", 200, 203, true, Sort = 2)]
    [ProvideOptionPage(typeof(NanoOptionsPageDebugging), OptionsCategoryName, "Debugging", 200, 204, true, Sort = 3)]
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
        /// The category of the options pages in Tools › Options.
        /// </summary>
        public const string OptionsCategoryName = ".NET nanoFramework";

        /// <summary>
        /// Path for nanoFramework Extension directory
        /// </summary>
        public static string NanoFrameworkExtensionDirectory { get; private set; }

        /// <summary>
        /// Version of the nanoFramework Extension DLL
        /// </summary>
        public static Version NanoFrameworkExtensionVersion { get; private set; }

        private static INanoDeviceCommService _nanoDeviceCommService;
        private static IVirtualDeviceService _virtualDeviceService;

        // registry key of the settings before they moved to the Settings Store
        private const string LegacySettingsSubkey = "nanoFrameworkExtension";

        /// <summary>
        /// The Device Explorer view model, created when the package is initialized.
        /// To be used by providers and other classes in the package.
        /// </summary>
        public static DeviceExplorerViewModel DeviceExplorerViewModel { get; private set; }

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
            await base.InitializeAsync(cancellationToken, progress);

            // the settings are read by everything that follows
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            NanoSettings.Initialize(this);
            await TaskScheduler.Default;

            TelemetryStartup.Initialize();

            DeleteLegacySettings();

            // the Device Explorer view model is owned by the package
            DeviceExplorerViewModel = new DeviceExplorerViewModel { Package = this };

            AddService(typeof(NanoDeviceCommService), CreateNanoDeviceCommServiceAsync);
            AddService(typeof(VirtualDeviceService), CreateVirtualDeviceManagerServiceAsync);

            await MessageCentre.InitializeAsync(this, ".NET nanoFramework Extension");

            DeployProvider.Initialize(this);
            UpdateManager.Initialize(this);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Enable debugger UI context
            UIContext.FromUIContextGuid(CorDebug.EngineGuid).IsActive = true;

            await DeviceOperations.InitializeAsync(this);
            VirtualDeviceService.InitVirtualDeviceAsync().FireAndForget(); ;
            TelemetryStartup.OnPackageLoadedAsync(this).FireAndForget();
        }

        #endregion

        /// <summary>
        /// Shows an options page of the extension in Tools › Options.
        /// </summary>
        /// <typeparam name="T">The options page.</typeparam>
        public static async Task ShowOptionPageAsync<T>() where T : NanoOptionsPage
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            s_instance.ShowOptionPage(typeof(T));
        }

        // the settings were in the registry before, they aren't imported
        private void DeleteLegacySettings()
        {
            try
            {
                UserRegistryRoot.DeleteSubKeyTree(LegacySettingsSubkey, false);
            }
            catch
            {
                // not important, it's left there
            }
        }

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

                TelemetryStartup.Shutdown();
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
