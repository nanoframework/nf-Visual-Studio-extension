// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Compatibility;
using nanoFramework.Tools.Debugger.NFDevice;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
    internal class DeployProvider : IDeployProvider
    {
        private const int ExclusiveAccessTimeout = 3000;

        private static Package _package;

        private static string _informationalVersionAttributeStore;

        private static string ExtensionInformationalVersion
        {
            get
            {
                if (_informationalVersionAttributeStore == null)
                {
                    // get details about assembly
                    _informationalVersionAttributeStore = (Attribute.GetCustomAttribute(
                        System.Reflection.Assembly.GetExecutingAssembly(),
                        typeof(AssemblyInformationalVersionAttribute))
                        as AssemblyInformationalVersionAttribute).InformationalVersion;
                }

                return _informationalVersionAttributeStore;
            }
        }

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

        public static void Initialize(AsyncPackage package)
        {
            _package = package;
        }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter outputPaneWriter)
        {
            List<byte[]> assemblies = new List<byte[]>();
            string targetFlashDumpFileName = "";

            await Task.Yield();

            // output information about assembly running this to help debugging
            MessageCentre.InternalErrorWriteLine($"Starting deployment transaction from v{ExtensionInformationalVersion}");

            //... we need to access the project name using reflection (step by step)
            // get type for ConfiguredProject
            var projSystemType = ConfiguredProject.GetType();

            // get private property MSBuildProject
            var buildProject = projSystemType.GetTypeInfo().GetDeclaredProperty("MSBuildProject");

            // get value of MSBuildProject property from ConfiguredProject object
            // this result is of type Microsoft.Build.Evaluation.Project
            var projectResult = await ((System.Threading.Tasks.Task<Microsoft.Build.Evaluation.Project>)buildProject.GetValue(Properties.ConfiguredProject));

            // All the files that needs potentially to be deployed to the internal storage
            var contents = projectResult.Items.Where(m => m.ItemType == "Content" && m.Metadata.Any(t => t.Name == "CopyToOutputDirectory" && t.EvaluatedValue != "Never"));

            if (!string.Equals(projectResult.Properties.First(p => p.Name == "OutputType").EvaluatedValue, "Exe", StringComparison.InvariantCultureIgnoreCase))
            {
                // This is not an executable project, it must be a referenced assembly

                MessageCentre.InternalErrorWriteLine($"Skipping deploy of project '{projectResult.FullPath}' because it is not an executable project.");

                return;
            }

            var deviceExplorer = Ioc.Default.GetService<DeviceExplorerViewModel>();

            // just in case....
            if (deviceExplorer.SelectedDevice == null)
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
            await outputPaneWriter.WriteLineAsync($"Getting things ready to deploy assemblies to .NET nanoFramework device: {device.Description}.");

            bool needsToCloseMessageOutput = false;

            // Get exclusive access to the device, but don't wait forever
            MessageCentre.InternalErrorWriteLine("Try to get exclusive access to the nanoDevice");

            using var exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(device, ExclusiveAccessTimeout)
                ?? throw new DeploymentException($"Couldn't access the device {device.Description}, it is used by another application!");

            try
            {
                MessageCentre.InternalErrorWriteLine("Starting debug engine on nanoDevice");

                // check if debugger engine exists
                if (NanoDeviceCommService.Device.DebugEngine == null)
                {
                    NanoDeviceCommService.Device.CreateDebugEngine();
                }

                await Task.Yield();

                var logProgressIndicator = new Progress<string>(MessageCentre.InternalErrorWriteLine);
                var progressIndicator = new Progress<MessageWithProgress>((m) => MessageCentre.StartMessageWithProgress(m));

                MessageCentre.InternalErrorWrite("Connecting to debugger engine...");
                needsToCloseMessageOutput = true;

                // if this is a serial virtual device, ping to check if it's responsive
                // this will happen in case the virtual device setting is ON
                if (NanoFrameworkPackage.SettingVirtualDeviceEnable
                    && device.Description.StartsWith("Virtual nanoDevice @ COM")
                    && device.Ping() != Debugger.WireProtocol.ConnectionSource.nanoCLR)
                {
                    // doesn't seem to be... better try to launch it
                    await NanoFrameworkPackage.VirtualDeviceService.StartVirtualDeviceAsync(false);
                }

                // connect to the device
                if (device.DebugEngine.Connect(false, true))
                {
                    needsToCloseMessageOutput = false;

                    MessageCentre.InternalErrorWriteAndCloseMessage("OK");

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
                        MessageCentre.InternalErrorWriteLine("*** ERROR: device reporting no assemblies loaded. This can not happen. Sanity check failed ***");

                        // there are no assemblies deployed?!
                        throw new DeploymentException($"Couldn't find any native assemblies deployed in {deviceExplorer.SelectedDevice.Description}! If the situation persists reboot the device.");
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

                    // build a list with the PE file corresponding to each DLL, referenced DLL and EXE
                    List<DeploymentAssembly> assemblyList = new List<DeploymentAssembly>();

                    foreach (string assemblyPath in assemblyPathsToDeploy)
                    {
                        string pePath = Path.ChangeExtension(assemblyPath, ".pe");

                        // read the assembly version from the PE header
                        PeAssemblyInfo peAssembly;

                        try
                        {
                            peAssembly = PeFileReader.ReadFile(pePath)[0];
                        }
                        catch (InvalidDataException ex)
                        {
                            MessageCentre.InternalErrorWriteLine($"*** ERROR: {ex.Message} ***");

                            throw new DeploymentException($"Deploy failed. {ex.Message} Please rebuild the solution.");
                        }

                        assemblyList.Add(new DeploymentAssembly(pePath, peAssembly.Version.ToString(4)));
                    }

                    // if there are referenced projects, the assembly list contains repeated assemblies so need to use Linq Distinct()
                    // an IEqualityComparer is required implementing the proper comparison
                    List<DeploymentAssembly> peCollection = assemblyList.Distinct(new DeploymentAssemblyDistinctEquality()).ToList();

                    await Task.Yield();

                    // check that the device firmware has the native assemblies required by the PEs to deploy
                    // and that all the assembly references can be resolved with the PEs to deploy
                    // (the PE format required is the one reported by the device firmware)
                    CompatibilityCheckResult compatibility = DeploymentCompatibility.Check(
                        peCollection.Select(a => a.Path),
                        device);

                    if (!compatibility.IsCompatible)
                    {
                        foreach (CompatibilityIssue issue in compatibility.Issues)
                        {
                            MessageCentre.InternalErrorWriteLine(issue.Description);
                        }

                        // can't deploy
                        throw new DeploymentException(compatibility.FormatMessage());
                    }

                    await Task.Yield();

                    // Keep track of total assembly size
                    long totalSizeOfAssemblies = 0;

                    MessageCentre.InternalErrorWriteLine($"Assemblies to deploy:");

                    // now we will re-deploy all system assemblies
                    foreach (DeploymentAssembly peItem in peCollection)
                    {
                        // append to the deploy blob the assembly
                        using (FileStream fs = File.Open(peItem.Path, FileMode.Open, FileAccess.Read))
                        {
                            long length = (fs.Length + 3) / 4 * 4;

                            await outputPaneWriter.WriteLineAsync($"Adding {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version} ({length} bytes) to deployment bundle");
                            MessageCentre.InternalErrorWriteLine($"Assembly: {Path.GetFileNameWithoutExtension(peItem.Path)} v{peItem.Version} ({length} bytes)");

                            byte[] buffer = new byte[length];

                            await Task.Yield();

                            await fs.ReadAsync(buffer, 0, (int)fs.Length);
                            assemblies.Add(buffer);

                            // Increment totalizer
                            totalSizeOfAssemblies += length;
                        }
                    }

                    await outputPaneWriter.WriteLineAsync($"Deploying {peCollection.Count:N0} assemblies to device... Total size in bytes is {totalSizeOfAssemblies}.");

                    MessageCentre.InternalErrorWriteLine($"Deploying {peCollection.Count:N0} assemblies to device");

                    // need to keep a copy of the deployment blob for the second attempt (if needed)
                    var assemblyCopy = new List<byte[]>(assemblies);

                    await Task.Yield();

                    await Task.Run(async delegate
                    {
                        // no need to reboot device
                        if (!device.DebugEngine.DeploymentExecute(
                            assemblyCopy,
                            false,
                            false,
                            progressIndicator,
                            logProgressIndicator))
                        {
                            // if the first attempt fails, give it another try

                            // wait before next pass
                            await Task.Delay(TimeSpan.FromSeconds(1));

                            await Task.Yield();

                            MessageCentre.InternalErrorWriteLine("Trying again to deploying assemblies");

                            // !! need to use the deployment blob copy
                            assemblyCopy = new List<byte[]>(assemblies);

                            // can't skip erase
                            // no need to reboot device
                            if (!device.DebugEngine.DeploymentExecute(
                                assemblyCopy,
                                false,
                                false,
                                progressIndicator,
                                logProgressIndicator))
                            {
                                MessageCentre.InternalErrorWriteLine("*** ERROR: deployment failed ***");

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
                    await outputPaneWriter.WriteLineAsync("Deployment successful!");

                    // Now deploying to the internal storage if any
                    if (contents.Any())
                    {
                        await outputPaneWriter.WriteLineAsync($"Deploying {contents.Count()} content files to internal storage");
                        MessageCentre.InternalErrorWriteLine("Deploying content files to internal storage");
                        foreach (var file in contents)
                        {
                            string fileName;
                            var storPath = file.Metadata.Where(m => m.Name == "NF_StoragePath");
                            if (storPath.Any())
                            {
                                fileName = storPath.FirstOrDefault().EvaluatedValue;
                            }
                            else
                            {
                                // Default is internal storage
                                fileName = "I:\\" + file.EvaluatedInclude;
                            }

                            await outputPaneWriter.WriteLineAsync($"{file.EvaluatedInclude} deploying to {fileName}.");
                            MessageCentre.InternalErrorWriteLine($"{file.EvaluatedInclude} deploying to {fileName}.");

                            // Find the file where the exe is. There is an exe because otherwise, we won't be here with a simple DLL
                            var fileAssemblyPath = projectResult.Properties.Where(m => m.Name == "TargetPath").First().EvaluatedValue;
                            var contentFileName = Path.Combine(fileAssemblyPath.Substring(0, fileAssemblyPath.LastIndexOf(Path.DirectorySeparatorChar)), file.EvaluatedInclude);

                            // Deploying the file
                            var ret = device.DebugEngine.AddStorageFile(fileName, File.ReadAllBytes(contentFileName));
                            if (ret == Debugger.WireProtocol.StorageOperationErrorCode.NoError)
                            {
                                await outputPaneWriter.WriteLineAsync($"{file.EvaluatedInclude} deployed sucessfully.");
                                MessageCentre.InternalErrorWriteLine($"{file.EvaluatedInclude} deployed sucessfully.");
                            }
                            else
                            {
                                await outputPaneWriter.WriteLineAsync($"{file.EvaluatedInclude} deployment error.");
                                MessageCentre.InternalErrorWriteLine($"{file.EvaluatedInclude} deployment error.");
                            }
                        }
                    }

                    // reset the hash for the connected device so the deployment information can be refreshed
                    deviceExplorer.LastDeviceConnectedHash = 0;
                }
                else
                {
                    MessageCentre.InternalErrorWriteAndCloseMessage("");
                    MessageCentre.InternalErrorWriteLine("*** ERROR: failing to connect to device ***");

                    // throw exception to signal deployment failure
                    throw new DeploymentException($"{deviceExplorer.SelectedDevice.Description} is not responding. Please retry the deployment. If the situation persists reboot the device.");
                }
            }
            catch (DeploymentException)
            {
                // this exception is used to flag a failed deployment to VS, no need to show anything about the exception here
                throw;
            }
            catch (Exception ex)
            {
                if (needsToCloseMessageOutput)
                {
                    MessageCentre.InternalErrorWriteAndCloseMessage("");
                }

                MessageCentre.InternalErrorWriteLine($"Unhandled exception with deployment provider:" +
                    $"{Environment.NewLine} {ex.Message} " +
                    $"{Environment.NewLine} {ex.InnerException} " +
                    $"{Environment.NewLine} {ex.StackTrace}");

#pragma warning disable S112 // OK to throw exception here to properly report back to the UI
                throw new Exception("Unexpected error. Please retry the deployment. If the situation persists reboot the device.");
#pragma warning restore S112 // General exceptions should never be thrown
            }
            finally
            {
                device.DebugEngine?.Stop();

                MessageCentre.StopProgressMessage();
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
            // required by the SDK
        }

        public void Rollback()
        {
            // required by the SDK
        }
    }
}
