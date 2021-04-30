//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
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
using System.Text.RegularExpressions;
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

        [Import]
        UnconfiguredProject UnconfiguredProject { get; set; }

        [Import]
        ConfiguredProject ConfiguredProject { get; set; }

        public static void Initialize(AsyncPackage package, ViewModelLocator vmLocator)
        {
            _package = package;
            _viewModelLocator = vmLocator;
        }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter outputPaneWriter)
        {
            List<byte[]> assemblies = new List<byte[]>();
            string targetFlashDumpFileName = "";
            int retryCount = 0;

            await Task.Yield();

            //... we need to access the project name using reflection (step by step)
            // get type for ConfiguredProject
            var projSystemType = ConfiguredProject.GetType();

            // get private property MSBuildProject
            var buildProject = projSystemType.GetTypeInfo().GetDeclaredProperty("MSBuildProject");

            // get value of MSBuildProject property from ConfiguredProject object
            // this result is of type Microsoft.Build.Evaluation.Project
            var projectResult = await ((System.Threading.Tasks.Task<Microsoft.Build.Evaluation.Project>)buildProject.GetValue(Properties.ConfiguredProject));

            if (!string.Equals(projectResult.Properties.First(p => p.Name == "OutputType").EvaluatedValue, "Exe", StringComparison.InvariantCultureIgnoreCase))
            {
                // This is not an executable project, it must be a referenced assembly

                MessageCentre.InternalErrorMessage($"Skipping deploy for project '{projectResult.FullPath}' because it is not an executable project.");

                return;
            }

            // just in case....
            if (_viewModelLocator?.DeviceExplorer.SelectedDevice == null)
            {
                // can't debug
                // throw exception to signal deployment failure
#pragma warning disable S112 // OK to use Exception here
                throw new Exception("There is no device selected. Please select a device in Device Explorer tool window.");
#pragma warning restore S112 // General exceptions should never be thrown
            }

            // get the device here so we are not always carrying the full path to the device
            NanoDeviceBase device = NanoDeviceCommService.Device;

            // user feedback
            await outputPaneWriter.WriteLineAsync($"Getting things ready to deploy assemblies to nanoFramework device: {device.Description}.");

            // device needs to be in 'initialized state' for a successful and correct deployment 
            // meaning that is not running nor stopped
            bool deviceIsInInitializeState = false;

            try
            {
                MessageCentre.InternalErrorMessage("Check and start debug engine on nanoDevice.");

                // check if debugger engine exists
                if (NanoDeviceCommService.Device.DebugEngine == null)
                {
                    NanoDeviceCommService.Device.CreateDebugEngine();
                }

                await Task.Yield();

                var logProgressIndicator = new Progress<string>(MessageCentre.InternalErrorMessage);
                var progressIndicator = new Progress<MessageWithProgress>((m) => MessageCentre.StartMessageWithProgress(m));

                // connect to the device
                if (device.DebugEngine.Connect())
                {
                    MessageCentre.InternalErrorMessage("Connect successful.");

                    await Task.Yield();

                    var eraseResult = await Task.Run(delegate
                    {
                        MessageCentre.InternalErrorMessage("Erase deployment block storage.");

                        return device.Erase(
                            EraseOptions.Deployment,
                            progressIndicator,
                            logProgressIndicator);
                    });

                    MessageCentre.StopProgressMessage();

                    // erase the target deployment area to ensure a clean deployment and execution start
                    if (eraseResult)
                    {
                        MessageCentre.InternalErrorMessage("Erase deployment area successful.");

                        // initial check 
                        if (device.DebugEngine.IsDeviceInInitializeState())
                        {
                            MessageCentre.InternalErrorMessage("Device status verified as being in initialized state. Requesting to resume execution.");

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
                                MessageCentre.InternalErrorMessage("Device has completed initialization.");

                                // done here
                                deviceIsInInitializeState = false;
                                break;
                            }

                            MessageCentre.InternalErrorMessage($"Waiting for device to report initialization completed ({retryCount}/{_numberOfRetries}).");

                            // provide feedback to user on the 1st pass
                            if (retryCount == 0)
                            {
                                await outputPaneWriter.WriteLineAsync(ResourceStrings.WaitingDeviceInitialization);
                            }

                            if (device.DebugEngine.IsConnectedTonanoBooter)
                            {
                                MessageCentre.InternalErrorMessage("Device reported running nanoBooter. Requesting to load nanoCLR.");

                                // request nanoBooter to load CLR
                                device.DebugEngine.ExecuteMemory(0);
                            }
                            else if (device.DebugEngine.IsConnectedTonanoCLR)
                            {
                                MessageCentre.InternalErrorMessage("Device reported running nanoCLR. Requesting to reboot nanoCLR.");

                                await Task.Run(delegate
                                {
                                    // already running nanoCLR try rebooting the CLR
                                    device.DebugEngine.RebootDevice(RebootOptions.ClrOnly);
                                });
                            }

                            // wait before next pass
                            // use a back-off strategy of increasing the wait time to accommodate slower or less responsive targets (such as networked ones)
                            await Task.Delay(TimeSpan.FromMilliseconds(_timeoutMiliseconds * (retryCount + 1)));

                            await Task.Yield();
                        }

                        // check if device is still in initialized state
                        if (!deviceIsInInitializeState)
                        {
                            // device has left initialization state
                            await outputPaneWriter.WriteLineAsync(ResourceStrings.DeviceInitialized);

                            await Task.Yield();

                            // do we have to generate a deployment image?
                            if (NanoFrameworkPackage.SettingGenerateDeploymentImage)
                            {
                                await Task.Run(async delegate
                                {
                                    targetFlashDumpFileName = await DeploymentImageGenerator.RunPreparationStepsToGenerateDeploymentImageAsync(device, Properties.ConfiguredProject, outputPaneWriter);
                                });
                            }

                            //////////////////////////////////////////////////////////
                            // sanity check for devices without native assemblies ?!?!
                            if (device.DeviceInfo.NativeAssemblies.Count == 0)
                            {
                                MessageCentre.InternalErrorMessage("Device reporting no assemblies loaded. This can not happen. Sanity check failed.");

                                // there are no assemblies deployed?!
                                throw new DeploymentException($"Couldn't find any native assemblies deployed in {_viewModelLocator.DeviceExplorer.SelectedDevice.Description}! If the situation persists reboot the device.");
                            }

                            MessageCentre.InternalErrorMessage("Computing deployment blob.");

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

                            // Starting with the StartUp project, collect all assemblies to be deployed.
                            // This will only add assemblies of projects which are actually referenced directly or
                            // indirectly by the StartUp project. Any project in the solution which is not referenced
                            // directly or indirectly by the StartUp project will not be included in the list of assemblies
                            // to be deployed.
                            await ReferenceCrawler.CollectAssembliesToDeployAsync(
                                configuredProjectsByOutputAssemblyPath,
                                outputAssemblyPathsByConfiguredProject,
                                assemblyPathsToDeploy,
                                Properties.ConfiguredProject);

                            // build a list with the full path for each DLL, referenced DLL and EXE
                            List<DeploymentAssembly> assemblyList = new List<DeploymentAssembly>();

                            // set decompiler options
                            // - don't load assembly in memory (causes issues with next solution rebuild)
                            // - don't throw resolution errors as we are not interested on this, just the assembly metadata
                            var decompilerSettings = new DecompilerSettings
                            {
                                LoadInMemory = false,
                                ThrowOnAssemblyResolveErrors = false
                            };

                            foreach (string assemblyPath in assemblyPathsToDeploy)
                            {
                                // load assembly in order to get the versions
                                var decompiler = new CSharpDecompiler(assemblyPath, decompilerSettings);
                                var assemblyProperties = decompiler.DecompileModuleAndAssemblyAttributesToString();

                                // read attributes using a Regex

                                // AssemblyVersion
                                string pattern = @"(?<=AssemblyVersion\("")(.*)(?=\""\)])";
                                var match = Regex.Matches(assemblyProperties, pattern, RegexOptions.IgnoreCase);
                                string assemblyVersion = match[0].Value;

                                // AssemblyNativeVersion
                                pattern = @"(?<=AssemblyNativeVersion\("")(.*)(?=\""\)])";
                                match = Regex.Matches(assemblyProperties, pattern, RegexOptions.IgnoreCase);

                                // only class libs have this attribute, therefore sanity check is required
                                string nativeVersion = "";
                                if (match.Count == 1)
                                {
                                    nativeVersion = match[0].Value;
                                }

                                assemblyList.Add(new DeploymentAssembly(assemblyPath, assemblyVersion, nativeVersion));
                            }

                            // if there are referenced project, the assembly list contains repeated assemblies so need to use Linq Distinct()
                            // an IEqualityComparer is required implementing the proper comparison
                            List<DeploymentAssembly> distinctAssemblyList = assemblyList.Distinct(new DeploymentAssemblyDistinctEquality()).ToList();

                            // build a list with the PE files corresponding to each DLL and EXE
                            List<DeploymentAssembly> peCollection = distinctAssemblyList.Select(a => new DeploymentAssembly(a.Path.Replace(".dll", ".pe").Replace(".exe", ".pe"), a.Version, a.NativeVersion)).ToList();

                            // build a list with the PE files corresponding to a DLL for native support checking
                            // only need to check libraries because EXEs don't have native counterpart
                            List<DeploymentAssembly> peCollectionToCheck = distinctAssemblyList.Where(i => i.Path.EndsWith(".dll")).Select(a => new DeploymentAssembly(a.Path.Replace(".dll", ".pe"), a.Version, a.NativeVersion)).ToList();

                            await Task.Yield();

                            var checkAssembliesResult = await CheckNativeAssembliesAvailabilityAsync(device.DeviceInfo.NativeAssemblies, peCollectionToCheck);
                            if (checkAssembliesResult != "")
                            {
                                MessageCentre.InternalErrorMessage("Found assemblies mismatches when checking for deployment pre-check.");

                                // can't deploy
                                throw new DeploymentException(checkAssembliesResult);
                            }

                            await Task.Yield();

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

                                    await Task.Yield();

                                    await fs.ReadAsync(buffer, 0, (int)fs.Length);
                                    assemblies.Add(buffer);

                                    // Increment totalizer
                                    totalSizeOfAssemblies += length;
                                }
                            }

                            await outputPaneWriter.WriteLineAsync($"Deploying {peCollection.Count:N0} assemblies to device... Total size in bytes is {totalSizeOfAssemblies.ToString()}.");
                            MessageCentre.InternalErrorMessage("Deploying assemblies.");

                            // need to keep a copy of the deployment blob for the second attempt (if needed)
                            var assemblyCopy = new List<byte[]>(assemblies);

                            await Task.Yield();

                            await Task.Run(async delegate
                            {
                                // OK to skip erase as we just did that
                                // no need to reboot device
                                if (!device.DebugEngine.DeploymentExecute(
                                    assemblyCopy, 
                                    false,
                                    true,
                                    progressIndicator,
                                    logProgressIndicator))
                                {
                                    // if the first attempt fails, give it another try

                                    // wait before next pass
                                    await Task.Delay(TimeSpan.FromSeconds(1));

                                    await Task.Yield();

                                    MessageCentre.InternalErrorMessage("Deploying assemblies. Second attempt.");

                                    // !! need to use the deployment blob copy
                                    assemblyCopy = new List<byte[]>(assemblies);

                                    // can't skip erase as we just did that
                                    // no need to reboot device
                                    if (!device.DebugEngine.DeploymentExecute(
                                        assemblyCopy, 
                                        false,
                                        false,
                                        progressIndicator,
                                        logProgressIndicator))
                                    {
                                        MessageCentre.InternalErrorMessage("Deployment failed.");

                                        // throw exception to signal deployment failure
                                        throw new DeploymentException("Deploy failed.");
                                    }
                                }
                            });

                            await Task.Yield();

                            // do we have to generate a deployment image?
                            if (NanoFrameworkPackage.SettingGenerateDeploymentImage)
                            {
                                await Task.Run(async delegate
                                {
                                    await DeploymentImageGenerator.GenerateDeploymentImageAsync(device, targetFlashDumpFileName, assemblies, Properties.ConfiguredProject, outputPaneWriter);
                                });
                            }

                            // deployment successful
                            await outputPaneWriter.WriteLineAsync("Deployment successful.");

                            // reset the hash for the connected device so the deployment information can be refreshed
                            _viewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;
                        }
                        else
                        {
                            // after retry policy applied seems that we couldn't resume execution on the device...

                            MessageCentre.InternalErrorMessage("Failed to initialize device.");

                            // throw exception to signal deployment failure
                            throw new DeploymentException(ResourceStrings.DeviceInitializationTimeout);
                        }
                    }
                    else
                    {
                        // failed to erase deployment area, despite not critical, better abort

                        MessageCentre.InternalErrorMessage("Failing to erase deployment area.");

                        // throw exception to signal deployment failure
                        throw new DeploymentException(ResourceStrings.EraseTargetDeploymentFailed);
                    }
                }
                else
                {
                    MessageCentre.InternalErrorMessage("Failing to connect to device.");

                    // throw exception to signal deployment failure
                    throw new DeploymentException($"{_viewModelLocator.DeviceExplorer.SelectedDevice.Description} is not responding. Please retry the deployment. If the situation persists reboot the device.");
                }
            }
            catch (DeploymentException)
            {
                // this exception is used to flag a failed deployment to VS, no need to show anything about the exception here
                throw;
            }
            catch (Exception ex)
            {
                MessageCentre.InternalErrorMessage($"Unhandled exception with deployment provider:"  +
                    $"{Environment.NewLine} {ex.Message} " +
                    $"{Environment.NewLine} {ex.InnerException} " +
                    $"{Environment.NewLine} {ex.StackTrace}");

#pragma warning disable S112 // OK to throw exception here to properly report back to the UI
                throw new Exception("Unexpected error. Please retry the deployment. If the situation persists reboot the device.");
#pragma warning restore S112 // General exceptions should never be thrown
            }
            finally
            {
                MessageCentre.StopProgressMessage();
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
                    
                    // read the PE checksum from the byte array at position 0x14
                    byte[] buffer = new byte[4];
                    fs.Position = 0x14;
                    await fs.ReadAsync(buffer, 0, 4);
                    var nativeMethodsChecksum = BitConverter.ToUInt32(buffer, 0);

                    if (nativeMethodsChecksum == 0)
                    {
                        // PEs with native methods checksum equal to 0 DO NOT require native support 
                        // OK to move to the next one
                        continue;
                    }

                    // try to find a native assembly matching the checksum for this PE
                    if (nativeAssemblies.Exists(a => a.Checksum == nativeMethodsChecksum))
                    {
                        nativeAssembly = nativeAssemblies.Find(a => a.Checksum == nativeMethodsChecksum);

                        // now check the native version against the requested version on the PE
                        if (nativeAssembly.Version.ToString(4) == peItem.NativeVersion)
                        {
                            // we are good with this one
                            continue;
                        }

                        // no suitable native assembly found build a (hopefully) helpful message to the developer
                        wrongAssemblies += $"Couldn't find a valid native assembly required by {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version}, checksum 0x{nativeMethodsChecksum.ToString("X8")}." + Environment.NewLine +
                                        $"This project is referencing {Path.GetFileNameWithoutExtension(peItem.Path)} NuGet package requiring native v{peItem.NativeVersion}." + Environment.NewLine +
                                        $"The connected target has v{nativeAssembly.Version.ToString(4)}." + Environment.NewLine;
                    }
                    else
                    {
                        // no suitable native assembly found build a (hopefully) helpful message to the developer
                        missingAssemblies += $"Couldn't find a valid native assembly required by {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version}, checksum 0x{nativeMethodsChecksum.ToString("X8")}." + Environment.NewLine +
                                        $"This project is referencing {Path.GetFileNameWithoutExtension(peItem.Path)} NuGet package requiring native v{peItem.NativeVersion}." + Environment.NewLine +
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
                               $"  2) if the project is referring the appropriate version of the NuGet package." + Environment.NewLine;
            }

            if (!string.IsNullOrEmpty(missingAssemblies))
            {
                errorMessage += missingAssemblies;
                errorMessage += "Please check: " + Environment.NewLine +
                                    "  1) if the target is running the most updated image." + Environment.NewLine +
                                    "  2) if the target image was built to include support for all referenced assemblies." + Environment.NewLine;
            }


            // close error message, if needed
            if (!string.IsNullOrEmpty(errorMessage))
            {
                errorMessage += "" + Environment.NewLine;
                errorMessage += "If the target is running a PREVIEW version the projects have to reference PREVIEW NuGet packages." + Environment.NewLine;
                errorMessage += "Check the Visual Studio FAQ here: https://docs.nanoframework.net/content/faq/working-with-vs-extension.html" + Environment.NewLine;
                errorMessage += "" + Environment.NewLine;
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
            // required by the SDK
        }

        public void Rollback()
        {
            // required by the SDK
        }
    }
}
