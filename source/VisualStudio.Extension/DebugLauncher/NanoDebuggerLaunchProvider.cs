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
            var deployDeviceName = SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().SelectedDevice.Description;
            var portName = SimpleIoc.Default.GetInstance<DeviceExplorerViewModel>().SelectedTransportType.ToString();

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

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        {
            return TplExtensions.TrueTask;
        }

        private async Task<string> GetCommandLineForLaunchAsync()
        {
            CommandLineBuilder cb = new CommandLineBuilder();

            cb.AddArguments("/waitfordebugger");

            ///////////////////////////////////////////////////////
            // get the list of assemblies referenced by the project
            var referencedAssemblies = await Properties.ConfiguredProject.Services.AssemblyReferences.GetResolvedReferencesAsync();

            //////////////////////////////////////////////////////////////////////////
            // get the list of other projects referenced by the project being deployed
            var referencedProjects = await Properties.ConfiguredProject.Services.ProjectReferences.GetResolvedReferencesAsync();

            /////////////////////////////////////////////////////////
            // get the target path to reach the PE for the executable

            //... we need to access the target path using reflection (step by step)
            // get type for ConfiguredProject
            var projSystemType = Properties.ConfiguredProject.GetType();

            // get private property MSBuildProject
            var buildProject = projSystemType.GetTypeInfo().GetDeclaredProperty("MSBuildProject");

            // get value of MSBuildProject property from ConfiguredProject object
            // this result is of type Microsoft.Build.Evaluation.Project
            var projectResult = await ((System.Threading.Tasks.Task<Microsoft.Build.Evaluation.Project>)buildProject.GetValue(Properties.ConfiguredProject));

            // we want the target path property
            var targetPath = projectResult.Properties.First(p => p.Name == "TargetPath").EvaluatedValue;

            // build a list with the full path for each DLL, referenced DLL and EXE
            List<string> assemblyList = new List<string>();

            foreach (IAssemblyReference reference in referencedAssemblies)
            {
                assemblyList.Add(await reference.GetFullPathAsync());
            }

            // loop through each project that is set to build
            foreach (IBuildDependencyProjectReference project in referencedProjects)
            {
                if (await project.GetReferenceOutputAssemblyAsync())
                {
                    assemblyList.Add(await project.GetFullPathAsync());
                }
            }

            // now add the executable to this list
            assemblyList.Add(targetPath);

            // build a list with the PE files corresponding to each DLL and EXE
            List<string> peCollection = assemblyList.Select(a => a.Replace(".dll", ".pe").Replace(".exe", ".pe")).ToList();

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
