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
        private const int _timeoutMiliseconds = 1000;

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
                    // erase the target deployment area to ensure a clean deployment and execution start
                    if (await device.EraseAsync(EraseOptions.Deployment, CancellationToken.None))
                    {
                        // ESP32 seems to be a bit stubborn to restart a debug session after the previous one ends
                        // rebooting the device improves this behaviour
                        if (device.DebugEngine.Capabilities.SolutionReleaseInfo.targetVendorInfo.Contains("ESP32"))
                        {
                            // send reset command to device
                            device.DebugEngine.RebootDevice(RebootOptions.NormalReboot);

                            // give it a little rest to allow reboot to complete
                            await Task.Delay(TimeSpan.FromMilliseconds(2000));
                        }

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
                            List<DeploymentAssembly> assemblyList = new List<DeploymentAssembly>();

                            foreach (string assemblyPath in assemblyPathsToDeploy)
                            {
                                // load assembly to get the version
                                var assembly = Assembly.Load(File.ReadAllBytes(assemblyPath)).GetName();
                                assemblyList.Add(new DeploymentAssembly(assemblyPath, $"{assembly.Version.ToString(4)}"));
                            }

                            // if there are referenced project, the assembly list contains repeated assemblies so need to use Linq Distinct()
                            // an IEqualityComparer is required implementing the proper comparison
                            List<DeploymentAssembly> distinctAssemblyList = assemblyList.Distinct(new DeploymentAssemblyDistinctEquality()).ToList();

                            // build a list with the PE files corresponding to each DLL and EXE
                            List<DeploymentAssembly> peCollection = distinctAssemblyList.Select(a => new DeploymentAssembly(a.Path.Replace(".dll", ".pe").Replace(".exe", ".pe"), a.Version)).ToList();

                            var checkAssembliesResult = await CheckNativeAssembliesAvailabilityAsync(device.DeviceInfo.NativeAssemblies, peCollection);
                            if (checkAssembliesResult != "")
                            {
                                // can't deploy
                                throw new DeploymentException(checkAssembliesResult);
                            }

                            // Keep track of total assembly size
                            long totalSizeOfAssemblies = 0;

                            // now we will re-deploy all system assemblies
                            foreach (DeploymentAssembly peItem in peCollection)
                            {
                                // append to the deploy blob the assembly
                                using (FileStream fs = File.Open(peItem.Path, FileMode.Open, FileAccess.Read))
                                {
                                    long length = (fs.Length + 3) / 4 * 4;
                                    await outputPaneWriter.WriteLineAsync($"Adding {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version} ({length.ToString()} bytes) to deployment bundle");
                                    byte[] buffer = new byte[length];

                                    await fs.ReadAsync(buffer, 0, (int)fs.Length);
                                    assemblies.Add(buffer);

                                    // Increment totalizer
                                    totalSizeOfAssemblies += length;
                                }
                            }

                            await outputPaneWriter.WriteLineAsync($"Deploying {peCollection.Count:N0} assemblies to device... Total size in bytes is {totalSizeOfAssemblies.ToString()}.");

                            Thread.Yield();

                            if (!device.DebugEngine.DeploymentExecute(assemblies, false))
                            {
                                // give it another try, ESP32 seems to be a bit stubborn at times...

                                // wait before next pass
                                await Task.Delay(TimeSpan.FromSeconds(1));

                                Thread.Yield();

                                if (!device.DebugEngine.DeploymentExecute(assemblies, false))
                                {
                                    // throw exception to signal deployment failure
                                    throw new DeploymentException("Deploy failed.");
                                }
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
                        // failed to erase deployment area, despite not critical, better abort
                        // throw exception to signal deployment failure
                        throw new DeploymentException(ResourceStrings.EraseTargetDeploymentFailed);
                    }
                }
                else
                {
                    // throw exception to signal deployment failure
                    throw new DeploymentException($"{_viewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding. Please retry the deployment. If the situation persists reboot the device.");
                }
            }
            catch (DeploymentException ex)
            {
                throw ex;
            }
            catch (Exception ex)
            {
                MessageCentre.InternalErrorMessage($"Unhandled exception with deployment provider: {ex.Message}.");

                throw new Exception("Unexpected error. Please retry the deployment. If the situation persists reboot the device.");
            }
        }

        private async System.Threading.Tasks.Task<string> CheckNativeAssembliesAvailabilityAsync(List<CLRCapabilities.NativeAssemblyProperties> nativeAssemblies, List<DeploymentAssembly> peCollection)
        {
            string errorMessage = string.Empty;
            string wrongAssemblies = string.Empty;
            string missingAssemblies = string.Empty;

            // loop through each PE to deploy...
            foreach (var peItem in peCollection)
            {
                // open the PE file and load content
                using (FileStream fs = File.Open(peItem.Path, FileMode.Open, FileAccess.Read))
                {
                    CLRCapabilities.NativeAssemblyProperties nativeAssembly;
                    // get PE checksum
                    byte[] buffer = new byte[4];
                    fs.Position = 0x14;
                    await fs.ReadAsync(buffer, 0, 4);
                    var peChecksum = BitConverter.ToUInt32(buffer, 0);

                    if (peChecksum == 0)
                    {
                        // only PEs with checksum are class libraries so we can move to the next one
                        continue;
                    }

                    // try to find a native assembly matching the checksum for this PE
                    if (nativeAssemblies.Exists(a => a.Checksum == peChecksum))
                    {
                        nativeAssembly = nativeAssemblies.Find(a => a.Checksum == peChecksum);

                        // check the version now
                        if (nativeAssembly.Version.ToString(4) == peItem.Version)
                        {
                            // we are good with this one
                            continue;
                        }

                        // no suitable native assembly found build a (hopefully) helpful message to the developer
                        wrongAssemblies += $"Couldn't find a valid native assembly required by {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version}, checksum 0x{peChecksum.ToString("X8")}." + Environment.NewLine +
                                        $"This project is referencing {Path.GetFileNameWithoutExtension(peItem.Path)} NuGet package with v{peItem.Version}." + Environment.NewLine +
                                        $"The connected target is has support for v{nativeAssembly.Version.ToString(4)}." + Environment.NewLine;
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

                        if (exceptionAssemblies.Exists(a => a.Name == Path.GetFileNameWithoutExtension(peItem.Path)))
                        {
                            // we are good with this one
                            continue;
                        }

                        // no suitable native assembly found build a (hopefully) helpful message to the developer
                        missingAssemblies = $"Couldn't find a valid native assembly required by {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version}, checksum 0x{peChecksum.ToString("X8")}." + Environment.NewLine +
                                        $"This project is referencing {Path.GetFileNameWithoutExtension(peItem.Path)} NuGet package with v{peItem.Version}." + Environment.NewLine +
                                        $"The connected target does not have support for {Path.GetFileNameWithoutExtension(peItem.Path)}." + Environment.NewLine;

                    }
                }
            }


            if (!string.IsNullOrEmpty(wrongAssemblies) ||
                !string.IsNullOrEmpty(missingAssemblies))
            {
                // init error message
                errorMessage = "Deploy failed." + Environment.NewLine +
                                "***************************************" + Environment.NewLine;
            }

            if (!string.IsNullOrEmpty(wrongAssemblies))
            {
                errorMessage += wrongAssemblies;
                errorMessage += $"Please check: " + Environment.NewLine +
                               $"  1) if the target is running the most updated image." + Environment.NewLine +
                               $"  2) if the project is referring the appropriate version of the assembly." + Environment.NewLine;
            }

            if (string.IsNullOrEmpty(missingAssemblies))
            {
                errorMessage += missingAssemblies;
                errorMessage += "Please check: " + Environment.NewLine +
                                    "  1) if the target is running the most updated image." + Environment.NewLine +
                                    "  2) if the target image was built to include support for referenced assembly." + Environment.NewLine;
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
