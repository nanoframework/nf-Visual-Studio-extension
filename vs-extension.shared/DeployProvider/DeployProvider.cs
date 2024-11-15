// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
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

                        // read attributes using a RegEx

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

                    // if there are referenced projects, the assembly list contains repeated assemblies so need to use Linq Distinct()
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
                        // can't deploy
                        throw new DeploymentException(checkAssembliesResult);
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

        private async System.Threading.Tasks.Task<string> CheckNativeAssembliesAvailabilityAsync(
            List<CLRCapabilities.NativeAssemblyProperties> nativeAssemblies,
            List<DeploymentAssembly> peCollection)
        {
            string errorMessage = string.Empty;
            string wrongAssemblies = "The connected target has the wrong version for the following assembly(ies):" + Environment.NewLine + Environment.NewLine;
            string missingAssemblies = "The connected target does not have support for the following assembly(ies):" + Environment.NewLine + Environment.NewLine;
            int wrongAssembliesCount = 0;
            int missingAssembliesCount = 0;

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

                    // try to find a native assembly...
                    nativeAssembly = nativeAssemblies.Find(a => a.Name == Path.GetFileNameWithoutExtension(peItem.Path));

                    if (nativeAssembly.Name != null)
                    {
                        // matching the checksum and version for this PE
                        if (nativeAssembly.Checksum == nativeMethodsChecksum
                            && nativeAssembly.Version.ToString(4) == peItem.NativeVersion)
                        {
                            // we are good with this one
                            continue;
                        }

                        wrongAssembliesCount++;

                        // no suitable native assembly found build a (hopefully) helpful message to the developer
                        wrongAssemblies += $"    '{Path.GetFileNameWithoutExtension(peItem.Path)}' requires native v{peItem.NativeVersion}, checksum 0x{nativeMethodsChecksum:X8}." + Environment.NewLine +
                                        $"    Connected target has v{nativeAssembly.Version.ToString(4)}, checksum 0x{nativeAssembly.Checksum:X8}." + Environment.NewLine + Environment.NewLine;

                        MessageCentre.InternalErrorWriteLine($"Version mismatch for {Path.GetFileNameWithoutExtension(peItem.Path)}. Need v{peItem.Version}, checksum 0x{nativeMethodsChecksum:X8}.");
                        MessageCentre.InternalErrorWriteLine($"The connected target has v{nativeAssembly.Version.ToString(4)}, checksum 0x{nativeAssembly.Checksum:X8}.");
                    }
                    else
                    {
                        missingAssembliesCount++;

                        // no suitable native assembly found build a (hopefully) helpful message to the developer
                        missingAssemblies += $"    '{Path.GetFileNameWithoutExtension(peItem.Path)}'" + Environment.NewLine;

                        MessageCentre.InternalErrorWriteLine($"The connected target does not have support for {Path.GetFileNameWithoutExtension(peItem.Path)}.");
                    }
                }
            }

            if (wrongAssembliesCount > 0 ||
                missingAssembliesCount > 0)
            {
                // init error message
                errorMessage = "Deploy failed." + Environment.NewLine + Environment.NewLine +
                                "***************************************************************************" + Environment.NewLine + Environment.NewLine;
            }

            if (wrongAssembliesCount > 0)
            {
                errorMessage += wrongAssemblies;
                errorMessage += $"Please check: " + Environment.NewLine +
                               $"  1) if the target is running the most updated image." + Environment.NewLine +
                               $"  2) if the project is referring the appropriate version of the NuGet package." + Environment.NewLine;
            }

            if (missingAssembliesCount > 0)
            {
                errorMessage += Environment.NewLine + missingAssemblies;
                errorMessage += Environment.NewLine + "Please check: " + Environment.NewLine +
                                    "  1) if the target is running the most updated image." + Environment.NewLine +
                                    "  2) if the target image was built to include support for all referenced assemblies." + Environment.NewLine;
            }

            // close error message, if needed
            if (!string.IsNullOrEmpty(errorMessage))
            {
                errorMessage += "" + Environment.NewLine;
                errorMessage += "Our Visual Studio FAQ has a troubleshooting guide: https://docs.nanoframework.net/content/faq/working-with-vs-extension.html" + Environment.NewLine;
                errorMessage += "" + Environment.NewLine;
                errorMessage += "***************************************************************************" + Environment.NewLine;
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
