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
using System.Diagnostics;
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
            // just in case....
            if ((_viewModelLocator?.DeviceExplorer.SelectedDevice == null))
            {
                // can't debug
                // throw exception to signal deployment failure
                throw new Exception("There is no device selected. Please select a device in Device Explorer tool window.");
            }

            // get the device here so we are not always carrying the full path to the device
            NanoDeviceBase device = NanoDeviceCommService.Device;

            // user feedback
            await outputPaneWriter.WriteLineAsync($"Getting things ready to deploy assemblies to nanoFramework device: {device.Description}.");

            List<byte[]> assemblies = new List<byte[]>();

            // device needs to be in 'initialized state' for a successful and correct deployment 
            // meaning that is not running nor stopped
            int retryCount = 0;
            bool deviceIsInInitializeState = false;

            try
            {
                // connect to the device
                if (await device.DebugEngine.ConnectAsync(5000))
                {

                    // initial check 
                    if (device.DebugEngine.IsDeviceInInitializeState())
                    {
                        // set flag
                        deviceIsInInitializeState = true;

                        // device is still in initialization state, try resume execution
                        device.DebugEngine.ResumeExecution();
                    }

                    // handle the workflow required to try resuming the execution on the device
                    // only required if device is not already there
                    // retry 5 times with a 200ms interval between retries
                    while (retryCount++ < _numberOfRetries && deviceIsInInitializeState)
                    {
                        if (!device.DebugEngine.IsDeviceInInitializeState())
                        {
                            // done here
                            deviceIsInInitializeState = false;
                            break;
                        }

                        // provide feedback to user on the 1st pass
                        if (retryCount == 0)
                        {
                            await outputPaneWriter.WriteLineAsync(ResourceStrings.WaitingDeviceInitialization);
                        }

                        if (device.DebugEngine.ConnectionSource == Tools.Debugger.WireProtocol.ConnectionSource.nanoBooter)
                        {
                            // request nanoBooter to load CLR
                            device.DebugEngine.ExecuteMemory(0);
                        }
                        else if (device.DebugEngine.ConnectionSource == Tools.Debugger.WireProtocol.ConnectionSource.nanoCLR)
                        {
                            // already running nanoCLR try rebooting the CLR
                            device.DebugEngine.RebootDevice(RebootOptions.ClrOnly);
                        }

                        // wait before next pass
                        await Task.Delay(TimeSpan.FromMilliseconds(_timeoutMiliseconds));
                    };

                    // check if device is still in initialized state
                    if (!deviceIsInInitializeState)
                    {
                        // device has left initialization state
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

                        // get version information of executable
                        var executableVersionInfo = FileVersionInfo.GetVersionInfo(targetPath);

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
                        assemblyList.Add((targetPath, $" v{ executableVersionInfo.ProductVersion }"));

                        // build a list with the PE files corresponding to each DLL and EXE
                        List<(string path, string version)> peCollection = assemblyList.Select(a => (a.path.Replace(".dll", ".pe").Replace(".exe", ".pe"), a.version)).ToList();

                        // Keep track of total assembly size
                        long totalSizeOfAssemblies = 0;

                        // now we will re-deploy all system assemblies
                        foreach ((string path, string version) peItem in peCollection)
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

                        if (!device.DebugEngine.DeploymentExecute(assemblies, false))
                        {
                            // throw exception to signal deployment failure
                            throw new Exception("Deploy failed.");
                        }

                        // deployment successful
                        await outputPaneWriter.WriteLineAsync("Deployment successful.");

                        // reset the hash for the connected device so the deployment information can be refreshed
                        _viewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;

                        // give sometime to the CLR to reboot
                        await Task.Delay(500);

                        // reboot device
                        await outputPaneWriter.WriteLineAsync("Rebooting nanoCLR on device.");

                        device.DebugEngine.RebootDevice(RebootOptions.ClrOnly);

                        // yield to give the UI thread a chance to respond to user input
                        await Task.Yield();

                        NanoDeviceCommService.Device.GetDeviceInfo(true);

                        // yield to give the UI thread a chance to respond to user input
                        await Task.Yield();

                        // provide feedback
                        await outputPaneWriter.WriteLineAsync("Reboot successful, device information updated.");
                    }
                    else
                    {
                        // after retry policy applied seems that we couldn't resume execution on the device...
                        // throw exception to signal deployment failure
                        throw new Exception(ResourceStrings.DeviceInitializationTimeout);
                    }
                }
                else
                {
                    // throw exception to signal deployment failure
                    throw new Exception($"{_viewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding. Please retry the deployment. If the situation persists reboot the device.");
                }
            }
            catch
            {
                throw new Exception("Unexpected error. Please retry the deployment. If the situation persists reboot the device.");
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
