// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.Debugger.NFDevice;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [ExportDebugger("NanoDebugger")]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal partial class NanoDebuggerLaunchProvider : DebugLaunchProviderBase
    {
        private const int ExclusiveAccessTimeout = 3000;

        private static AssemblyInformationalVersionAttribute _informationalVersionAttribute;

        [ImportingConstructor]
        public NanoDebuggerLaunchProvider(ConfiguredProject configuredProject)
            : base(configuredProject)
        {
            // get details about assembly
            _informationalVersionAttribute = Attribute.GetCustomAttribute(
                Assembly.GetExecutingAssembly(),
                typeof(AssemblyInformationalVersionAttribute))
                as AssemblyInformationalVersionAttribute;
        }

        [Import]
        IProjectService ProjectService { get; set; }

        public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            // output information about assembly running this to help debugging
            MessageCentre.InternalErrorWriteLine($"Launching debugger provider from v{_informationalVersionAttribute.InformationalVersion}");

            if (Ioc.Default.GetService<DeviceExplorerViewModel>().SelectedDevice != null)
            {
                var deployDeviceName = Ioc.Default.GetService<DeviceExplorerViewModel>().SelectedDevice.Description;

                // get device
                var device = Ioc.Default.GetService<DeviceExplorerViewModel>().SelectedDevice;

                var exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(device, ExclusiveAccessTimeout);
                if (exclusiveAccess is null)
                {
#pragma warning disable S112 // OK to use Exception here
                    throw new Exception($"Can't get access to {deployDeviceName}, another application is using the device!");
#pragma warning restore S112 // General exceptions should never be thrown            
                }
                else
                {
                    var stopDebugEngine = true;

                    try
                    {
                        // check for debug engine
                        if (device.DebugEngine == null)
                        {
                            device.CreateDebugEngine();
                        }

                        // update stack trace processing option
                        device.DebugEngine.NoStackTraceInExceptions = !NanoFrameworkPackage.DebuggingOptions.ProcessStackTraceOption;

                        // make sure that the device is connected
                        if (device.DebugEngine.Connect(
                                    false,
                                    true))
                        {
                            string commandLine = await GetCommandLineForLaunchAsync();
                            commandLine = string.Format("{0} \"{1}{2}\"", commandLine, CorDebugProcess.DeployDeviceName, deployDeviceName);

                            var settings = new DebugLaunchSettings(launchOptions)
                            {
                                Executable = typeof(CorDebugProcess).Assembly.Location,
                                Arguments = commandLine,
                                LaunchOperation = DebugLaunchOperation.CreateProcess,
                                PortSupplierGuid = DebugPortSupplier.PortSupplierGuid,
                                PortName = NanoFrameworkPackage.NanoDeviceCommService.Device.Description,
                                Project = VsHierarchy,
                                LaunchDebugEngineGuid = CorDebug.EngineGuid
                            };

                            stopDebugEngine = false;
                            return new IDebugLaunchSettings[] { settings };
                        }
                    }
                    finally
                    {
                        if (stopDebugEngine)
                        {
                            // On success, the debug engine does not have to be stopped, it will be stopped in the CorDebugProcess
                            // and the global exclusive access is terminated there.
                            device.DebugEngine?.Stop();
                        }

                        exclusiveAccess?.Dispose();
                    }
                }

#pragma warning disable S112 // OK to use Exception here
                throw new Exception($"Can't connect to {deployDeviceName}!");
#pragma warning restore S112 // General exceptions should never be thrown            
            }
            else
            {
#pragma warning disable S112 // OK to use Exception here
                throw new Exception("There is no device selected. Please select a device in Device Explorer tool window.");
#pragma warning restore S112 // General exceptions should never be thrown            
            }
        }

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        {
            return TplExtensions.TrueTask;
        }

        private async Task<string> GetCommandLineForLaunchAsync()
        {
            CommandLineBuilder cb = new CommandLineBuilder();

            cb.AddArguments("/waitfordebugger");

            // For a known project output assembly path, this shall contain the corresponding
            // ConfiguredProject:
            Dictionary<string, ConfiguredProject> configuredProjectsByOutputAssemblyPath =
                new Dictionary<string, ConfiguredProject>();

            // For a known ConfiguredProject, this shall contain the corresponding project output assembly
            // path:
            Dictionary<ConfiguredProject, string> outputAssemblyPathsByConfiguredProject =
                new Dictionary<ConfiguredProject, string>();


            // Fill these two dictionaries for all projects contained in the solution
            // (whether they belong to the deployment or not):
            await ReferenceCrawler.CollectProjectsAndOutputAssemblyPathsAsync(
                ProjectService,
                configuredProjectsByOutputAssemblyPath,
                outputAssemblyPathsByConfiguredProject);

            // This HashSet shall contain a list of full paths to all assemblies to be deployed, including
            // the compiled output assemblies of our solution's project and also all assemblies such as
            // NuGet packages referenced by those projects.
            // The HashSet will take care of only containing any string once even if added multiple times.
            // However, this is dependent on getting all paths always in the same casing.
            // Be aware that on file systems which ignore casing, we would end up having assemblies added
            // more than once here if the GetFullPathAsync() methods used below should not always reliably
            // return the path to the same assembly in the same casing.
            HashSet<string> assemblyPathsToDeploy = new HashSet<string>();

            // Starting with the startup project, collect all assemblies to be deployed.
            // This will only add assemblies of projects which are actually referenced directly or
            // indirectly by the startup project. Any project in the solution which is not referenced
            // directly or indirectly by the startup project will not be included in the list of assemblies
            // to be deployed.
            await ReferenceCrawler.CollectAssembliesToDeployAsync(
                configuredProjectsByOutputAssemblyPath,
                outputAssemblyPathsByConfiguredProject,
                assemblyPathsToDeploy,
                ConfiguredProject);


            // build a list with the full path for each DLL, referenced DLL and EXE
            List<string> assemblyList = new List<string>();

            foreach (string assemblyPath in assemblyPathsToDeploy)
            {
                assemblyList.Add(assemblyPath);
            }

            // if there are referenced project, the assembly list contains repeated assemblies so need to use Linq Distinct()
            // build a list with the PE files corresponding to each DLL and EXE
            List<string> peCollection = assemblyList.Distinct().Select(a => a.Replace(".dll", ".pe").Replace(".exe", ".pe")).ToList();

            foreach (string peFile in peCollection)
            {
                cb.AddArguments("/load:" + peFile);
            }

            string commandLine = cb.ToString();
            commandLine = Environment.ExpandEnvironmentVariables(commandLine);

            return commandLine;
        }
    }
}
