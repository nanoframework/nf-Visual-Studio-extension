//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.References;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.VisualStudio.Extension.Resources;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal class DeployProvider : IDeployProvider
    {
        // number of retries when performing a deploy operation
        private const int _numberOfRetries = 5;

        // timeout when performing a deploy operation
        private const int _timeoutMiliseconds = 200;

        private static ViewModelLocator _viewModelLocator;

        private static Package _package;

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider { get { return _package; } }

        INanoDeviceCommService NanoDeviceCommService { get { return ServiceProvider.GetService(typeof(NanoDeviceCommService)) as INanoDeviceCommService; } }

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        [Import]
        IProjectService ProjectService { get; set; }

        public static void Initialize(Package package, ViewModelLocator vmLocator)
        {
            _package = package;
            _viewModelLocator = vmLocator;
        }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter outputPaneWriter)
        {
            // make sure we're on the UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // just in case....
            if ((_viewModelLocator?.DeviceExplorer.SelectedDevice != null))
            {
                // can't debug
                // throw exception to signal deployment failure
                throw new Exception("There is no device selected. Please select a device in Device Explorer tool window.");
            }

            // get the device here so we are not always carrying the full path to the device
            NanoDeviceBase device = _viewModelLocator.DeviceExplorer.SelectedDevice;

            // user feedback
            await outputPaneWriter.WriteLineAsync($"Getting things ready to deploy assemblies to nanoFramework device: {device.Description}.");

            // flag to signal if device has any assemblies deployed
            bool hasAssembliesDeployed = device.DeviceInfo.Assemblies.Length > 0 ? true : false;

            List<byte[]> assemblies = new List<byte[]>();

            // device needs to be in 'initialized state' for a successful and correct deployment 
            // meaning that is not running nor stopped
            int retryCount = 0;
            bool deviceIsInInitializeState = false;

            // initial check 
            if (await device.DebugEngine.IsDeviceInInitializeStateAsync())
            {
                // set flag
                deviceIsInInitializeState = true;
            }
            else
            {
                // device is still in initialization state, try resume execution
                await device.DebugEngine.ResumeExecutionAsync();
            }

            // handle the workflow required to try setting the device in 'initialized state'
            // only required if device is not already there
            // retry 5 times with a 200ms interval between retries
            while (retryCount++ < _numberOfRetries && !deviceIsInInitializeState)
            {
                if (await device.DebugEngine.IsDeviceInInitializeStateAsync())
                {
                    // done here
                    deviceIsInInitializeState = true;
                    break;
                }

                // provide feedback to user on the 1st pass
                if (retryCount == 0)
                {
                    await outputPaneWriter.WriteLineAsync(ResourceStrings.WaitingDeviceInitialization);
                }

                if (device.DebugEngine.ConnectionSource == Debugger.WireProtocol.ConnectionSource.nanoBooter)
                {
                    // request nanoBooter to load CLR
                    await device.DebugEngine.ExecuteMemoryAsync(0);
                }
                else if (device.DebugEngine.ConnectionSource == Debugger.WireProtocol.ConnectionSource.nanoCLR)
                {
                    // already running nanoCLR try rebooting the CLR
                    await device.DebugEngine.RebootDeviceAsync(RebootOption.RebootClrWaitForDebugger);
                }

                // wait before next pass
                await Task.Delay(TimeSpan.FromMilliseconds(_timeoutMiliseconds));
            };

            // check if device is still in initialized state
            if (deviceIsInInitializeState)
            { 
                // device is initialized
                await outputPaneWriter.WriteLineAsync(ResourceStrings.DeviceInitialized);

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
                var projectResult = ((System.Threading.Tasks.Task<Microsoft.Build.Evaluation.Project>)buildProject.GetValue(Properties.ConfiguredProject));

                // we want the target path property
                var targetPath = projectResult.Result.Properties.First(p => p.Name == "TargetPath").EvaluatedValue;


                // build a list with the full path for each DLL, referenced DLL and EXE
                List<(string path, string version)> assemblyList = new List<(string path, string version)>();

                foreach (IAssemblyReference reference in referencedAssemblies)
                {
                    assemblyList.Add((await reference.GetFullPathAsync(), $" v{await reference.Metadata.GetEvaluatedPropertyValueAsync("Version")}"));
                }

                // loop through each project that is set to build
                foreach (IBuildDependencyProjectReference project in referencedProjects)
                {
                    if (await project.GetReferenceOutputAssemblyAsync())
                    {
                        assemblyList.Add((await project.GetFullPathAsync(), $" v{await project.Metadata.GetEvaluatedPropertyValueAsync("Version")}"));
                    }
                }

                // now add the executable to this list
                // TODO need to find where the version property is to add it here
                assemblyList.Add((targetPath, string.Empty));

                // build a list with the PE files corresponding to each DLL and EXE
                List<(string path, string version)> peCollection = assemblyList.Select(a => (a.path.Replace(".dll", ".pe").Replace(".exe", ".pe"), a.version)).ToList();

                // Keep track of total assembly size
                long totalSizeOfAssemblies = 0;

                // now we will re-deploy all system assemblies
                foreach((string path, string version) peItem in peCollection)
                {
                    // append to the deploy blob the assembly
                    using (FileStream fs = File.Open(peItem.path, FileMode.Open, FileAccess.Read))
                    {
                        long length = (fs.Length + 3) / 4 * 4;
                        await outputPaneWriter.WriteLineAsync($"Adding {Path.GetFileNameWithoutExtension(peItem.path) + peItem.version} ({length.ToString()} bytes) to deployment bundle");
                        byte[] buffer = new byte[length];

                        fs.Read(buffer, 0, (int)fs.Length);
                        assemblies.Add(buffer);

                        // Increment totalizer
                        totalSizeOfAssemblies += length;
                    }
                }

                await outputPaneWriter.WriteLineAsync($"Deploying assemblies to device...total size in bytes is {totalSizeOfAssemblies.ToString()}.");

                if (!await device.DebugEngine.DeploymentExecuteAsync(assemblies, false))
                {
                    // throw exception to signal deployment failure
                    throw new Exception("Deploy failed.");
                }

                // deployment successful
                await outputPaneWriter.WriteLineAsync("Deployment successful.");

                // reset the hash for the connected device so the deployment information can be refreshed
                _viewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                // reboot device
                if (hasAssembliesDeployed)
                {
                    await outputPaneWriter.WriteLineAsync("Rebooting nanoCLR on device.");

                    await ThreadHelper.JoinableTaskFactory.RunAsync(async () => { await device.DebugEngine.RebootDeviceAsync(RebootOption.RebootClrOnly); });
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // yield to give the UI thread a chance to respond to user input
                    await Task.Yield();

                    await NanoDeviceCommService.Device.GetDeviceInfoAsync(true);

                    // yield to give the UI thread a chance to respond to user input
                    await Task.Yield();

                    // provide feedback
                    await outputPaneWriter.WriteLineAsync("Reboot successful, device information updated.");
                }
                else
                {
                    await outputPaneWriter.WriteLineAsync("Soft rebooting device.");

                    // yield to give the UI thread a chance to respond to user input
                    await Task.Yield();

                    // set device to select
                    _viewModelLocator.DeviceExplorer.DeviceToReSelect = _viewModelLocator.DeviceExplorer.SelectedDevice.Description;

                    // reboot device
                    _viewModelLocator.DeviceExplorer.RebootSelectedDevice();

                    // yield to give the UI thread a chance to respond to user input
                    await Task.Yield();

                    await outputPaneWriter.WriteLineAsync("Soft reboot performed and reselection requested.");
                }
            }
            else
            {
                // after retry policy applied seems that we couldn't set the device in initialized state...
                // throw exception to signal deployment failure
                throw new Exception(ResourceStrings.DeviceInitializationTimeout);
            }
        }

        public bool IsDeploySupported
        {
            get
            {
                return true;
            }
        }

        public void Commit()
        {
        }

        public void Rollback()
        {
        }
    }
}
