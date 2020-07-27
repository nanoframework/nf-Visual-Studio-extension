//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight.Ioc;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.References;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [ExportDebugger(NanoDebugger.SchemaName)]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal partial class NanoDebuggerLaunchProvider : DebugLaunchProviderBase
    {
        [ImportingConstructor]
        public NanoDebuggerLaunchProvider(ConfiguredProject configuredProject)
            : base(configuredProject)
        {
        }

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        [Import]
        IProjectService ProjectService { get; set; }

        public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            try
            {
                var deployDeviceName = SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().SelectedDevice.Description;
                var portName = SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().SelectedTransportType.ToString();

                // make sure that the device is connected
                if (await SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().SelectedDevice.DebugEngine.ConnectAsync(5000))
                {
                    string commandLine = await GetCommandLineForLaunchAsync();
                    commandLine = string.Format("{0} \"{1}{2}\"", commandLine, CorDebugProcess.DeployDeviceName, deployDeviceName);

                    // The properties that are available via DebuggerProperties are determined by the property XAML files in your project.
                    var debuggerProperties = await Properties.GetNanoDebuggerPropertiesAsync();

                    var settings = new DebugLaunchSettings(launchOptions)
                    {
                        CurrentDirectory = await debuggerProperties.NanoDebuggerWorkingDirectory.GetEvaluatedValueAtEndAsync(),
                        Executable = typeof(CorDebugProcess).Assembly.Location,
                        Arguments = commandLine,
                        LaunchOperation = DebugLaunchOperation.CreateProcess,
                        PortSupplierGuid = DebugPortSupplier.PortSupplierGuid,
                        PortName = NanoFrameworkPackage.NanoDeviceCommService.Device.Description,
                        Project = VsHierarchy,
                        LaunchDebugEngineGuid = CorDebug.EngineGuid
                    };

                    return new IDebugLaunchSettings[] { settings };
                }
            }
            catch
            {
                // in case something goes wrong, we catch it here

            }

            return new List<DebugLaunchSettings>();
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
                Properties.ConfiguredProject);


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
