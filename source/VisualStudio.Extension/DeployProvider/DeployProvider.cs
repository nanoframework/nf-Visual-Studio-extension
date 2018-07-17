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
        private const int _timeoutMiliseconds = 500;

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

        public static void Initialize(AsyncPackage package, ViewModelLocator vmLocator)
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
                // check if debugger engine exists
                if (NanoDeviceCommService.Device.DebugEngine == null)
                {
                    NanoDeviceCommService.Device.CreateDebugEngine();
                }

                // connect to the device
                if (await device.DebugEngine.ConnectAsync(5000, true))
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
                    // retry 5 times with a 500ms interval between retries
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

                    Thread.Yield();

                    // check if device is still in initialized state
                    if (!deviceIsInInitializeState)
                    {
                        // device has left initialization state
                        await outputPaneWriter.WriteLineAsync(ResourceStrings.DeviceInitialized);

                        //////////////////////////////////////////////////////////
                        // sanity check for devices without native assemblies ?!?!
                        if (device.DeviceInfo.NativeAssemblies.Count == 0)
                        {
                            // there are no assemblies deployed?!
                            throw new DeploymentException($"Couldn't find any native assemblies deployed in {_viewModelLocator.DeviceExplorer.SelectedDevice.Description}! If the situation persists reboot the device.");
                        }

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

                        // get version information of executable
                        var executableVersionInfo = FileVersionInfo.GetVersionInfo(targetPath);

                        // build a list with the full path for each DLL, referenced DLL and EXE
                        List<(string path, string version)> assemblyList = new List<(string path, string version)>();

                        foreach (IAssemblyReference reference in referencedAssemblies)
                        {
                            // load assembly to get the version
                            var assembly = Assembly.Load(File.ReadAllBytes(await reference.GetFullPathAsync())).GetName();
                            assemblyList.Add((await reference.GetFullPathAsync(), $"{assembly.Version.ToString(4)}"));
                        }

                        // loop through each project that is set to build
                        foreach (IBuildDependencyProjectReference project in referencedProjects)
                        {
                            if (await project.GetReferenceOutputAssemblyAsync())
                            {
                                string referencedProjectAssemblyPath = await project.GetFullPathAsync();

                                // load assembly to get the version
                                var assembly = Assembly.Load(File.ReadAllBytes(referencedProjectAssemblyPath)).GetName();

                                // add the referenced project output to the assembly list
                                assemblyList.Add((referencedProjectAssemblyPath, $"{assembly.Version.ToString(4)}"));
                            }
                        }

                        // now add the executable to this list
                        assemblyList.Add((targetPath, $"{ executableVersionInfo.ProductVersion }"));

                        // if there are referenced project, the assembly list contains repeated assemblies so need to use Linq Distinct()
                        // build a list with the PE files corresponding to each DLL and EXE
                        List<(string path, string version)> peCollection = assemblyList.Distinct().Select(a => (a.path.Replace(".dll", ".pe").Replace(".exe", ".pe"), a.version)).ToList();

                        var checkAssembliesResult = await CheckNativeAssembliesAvailabilityAsync(device.DeviceInfo.NativeAssemblies, peCollection);
                        if (checkAssembliesResult != "")
                        {
                            // can't deploy
                            throw new DeploymentException(checkAssembliesResult);
                        }

                        // Keep track of total assembly size
                        long totalSizeOfAssemblies = 0;

                        // now we will re-deploy all system assemblies
                        foreach ((string path, string version) peItem in peCollection)
                        {
                            // append to the deploy blob the assembly
                            using (FileStream fs = File.Open(peItem.path, FileMode.Open, FileAccess.Read))
                            {
                                long length = (fs.Length + 3) / 4 * 4;
                                await outputPaneWriter.WriteLineAsync($"Adding {Path.GetFileNameWithoutExtension(peItem.path)} v{peItem.version} ({length.ToString()} bytes) to deployment bundle");
                                byte[] buffer = new byte[length];

                                await fs.ReadAsync(buffer, 0, (int)fs.Length);
                                assemblies.Add(buffer);

                                // Increment totalizer
                                totalSizeOfAssemblies += length;
                            }
                        }

                        await outputPaneWriter.WriteLineAsync($"Deploying assemblies to device...total size in bytes is {totalSizeOfAssemblies.ToString()}.");

                        Thread.Yield();

                        if (!device.DebugEngine.DeploymentExecute(assemblies, false))
                        {
                            // throw exception to signal deployment failure
                            throw new DeploymentException("Deploy failed.");
                        }

                        Thread.Yield();

                        // deployment successful
                        await outputPaneWriter.WriteLineAsync("Deployment successful.");

                        // reset the hash for the connected device so the deployment information can be refreshed
                        _viewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;
                    }
                    else
                    {
                        // after retry policy applied seems that we couldn't resume execution on the device...
                        // throw exception to signal deployment failure
                        throw new DeploymentException(ResourceStrings.DeviceInitializationTimeout);
                    }
                }
                else
                {
                    // throw exception to signal deployment failure
                    throw new DeploymentException($"{_viewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding. Please retry the deployment. If the situation persists reboot the device.");
                }
            }
            catch(DeploymentException ex)
            {
                throw ex;
            }
            catch(Exception ex)
            {
                MessageCentre.InternalErrorMessage($"Unhandled exception with deployment provider: {ex.Message}.");

                throw new Exception("Unexpected error. Please retry the deployment. If the situation persists reboot the device.");
            }
        }

        private async System.Threading.Tasks.Task<string> CheckNativeAssembliesAvailabilityAsync(List<CLRCapabilities.NativeAssemblyProperties> nativeAssemblies, List<(string path, string version)> peCollection)
        {
            string errorMessage = string.Empty;

            // loop through each PE to deploy...
            foreach(var peItem in peCollection)
            {
                // open the PE file and load content
                using (FileStream fs = File.Open(peItem.path, FileMode.Open, FileAccess.Read))
                {
                    // get PE checksum
                    byte[] buffer = new byte[4];
                    fs.Position = 0x14;
                    await fs.ReadAsync(buffer, 0, 4);
                    var peChecksum = BitConverter.ToUInt32(buffer, 0);

                    if(peChecksum == 0)
                    {
                        // only PEs with checksum are class libraries so we can move to the next one
                        continue;
                    }

                    // try to find a native assembly matching the checksum for this PE
                    if (nativeAssemblies.Exists(a => a.Checksum == peChecksum))
                    {
                        var nativeAssembly = nativeAssemblies.Find(a => a.Checksum == peChecksum);
                        
                        // check the version now
                        if(nativeAssembly.Version.ToString(4) == peItem.version)
                        {
                            // we are good with this one
                            continue;
                        }
                    }
                    else
                    {
                        // there are assemblies that don't have any native counterpart
                        // list those bellow so they aren't considered as not existing

                        List<CLRCapabilities.NativeAssemblyProperties> exceptionAssemblies = new List<CLRCapabilities.NativeAssemblyProperties>();

                        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                        // assemblies not having native implementation 
                        exceptionAssemblies.Add(new CLRCapabilities.NativeAssemblyProperties("Windows.Storage.Streams", 0, new Version()));
                        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                        if (exceptionAssemblies.Exists(a => a.Name == Path.GetFileNameWithoutExtension(peItem.path)))
                        {
                            // we are good with this one
                            continue;
                        }
                    }

                    if(string.IsNullOrEmpty(errorMessage))
                    {
                        // init error message
                        errorMessage =  "Deploy failed." + Environment.NewLine +
                                        "***************************************" + Environment.NewLine;
                    }

                    // no suitable native assembly found present a (hopefully) helpful message to the developer
                    errorMessage += $"Couldn't find a valid native assembly required by {Path.GetFileNameWithoutExtension(peItem.path)} v{peItem.version}, checksum 0x{peChecksum.ToString("X8")}." + Environment.NewLine;
                }
            }

            // close error message, if needed
            if (!string.IsNullOrEmpty(errorMessage))
            {
                errorMessage += "***************************************";
            }

            return errorMessage;
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
