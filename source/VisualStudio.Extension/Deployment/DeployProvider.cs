//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.References;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.VisualStudio.Extension.Resources;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using Polly;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
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

        private ViewModelLocator _viewModelLocator;
        ViewModelLocator ViewModelLocator
        {
            get
            {
                if (_viewModelLocator == null)
                {
                    if (System.Windows.Application.Current.TryFindResource("Locator") != null)
                    {
                        _viewModelLocator = (System.Windows.Application.Current.TryFindResource("Locator") as ViewModelLocator);
                    }
                }

                return _viewModelLocator;
            }
        }

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter outputPaneWriter)
        {
            // make sure we’re on the UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // just in case....
            if ((ViewModelLocator?.DeviceExplorer.ConnectionStateResult != ConnectionState.Connected))
            {
                // can't debug
                await outputPaneWriter.WriteLineAsync("There is no device connected. Check Device Explorer tool window.");
                return;
            }

            // get the device here so we are not always carrying the full path to the device
            NanoDeviceBase device = ViewModelLocator.DeviceExplorer.SelectedDevice;

            // user feedback
            await outputPaneWriter.WriteLineAsync($"Getting things ready to deploy assemblies to nanoFramework device: {device.Description}.");

            List<byte[]> assemblies = new List<byte[]>();
            HybridDictionary systemAssemblies = new HybridDictionary();

            // ensure that assemblies are loaded

            // build policy to check for device initialized
            // on false result
            // set retry count
            // set wait time between retries
            // on each retry send comand to ResumeExecution
            var deviceInInitStatePolicy = Policy.HandleResult(false).WaitAndRetryAsync(
                _numberOfRetries,
                retryAttempt => TimeSpan.FromMilliseconds(_timeoutMiliseconds),
                async (exception, timeSpan, retryCount, context) =>
                {
                    // send command to resune execution
                    await device.DebugEngine.ResumeExecutionAsync();

                    // provide feedback to user on the 1st pass
                    if (retryCount == 0)
                    {
                        await outputPaneWriter.WriteLineAsync(ResourceStrings.WaitingDeviceInitialization);
                    }
                });


            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // DEBUG warning: setting breakpoints on the code block bellow causes unhandled exception from the debug library becasue the async/await calls

            // check if device is initialized

            //Ensure Assemblies are loaded
            if (await IsDeviceInInitializeStateAsync(device.DebugEngine))
            {
                await device.DebugEngine.ResumeExecutionAsync();

                while (await IsDeviceInInitializeStateAsync(device.DebugEngine))
                {
                    await outputPaneWriter.WriteLineAsync(ResourceStrings.WaitingDeviceInitialization);

                    //need to break out of this or timeout or something?
                    // TODO 
                    await System.Threading.Tasks.Task.Delay(200);
                }
            }

            //Debug.WriteLine($"currentExecutionMode is {result.currentExecutionMode }");
            //Debug.WriteLine($"currentExecutionMode masked is {result.currentExecutionMode & Debugger.WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask }");

            //if (((result.currentExecutionMode & Debugger.WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask) == Debugger.WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Initialize))
            //{
            // device is in initialized state
            await outputPaneWriter.WriteLineAsync(ResourceStrings.DeviceInitialized);

            // try to resolve assemblies

            // Create cancelation token source
            CancellationTokenSource cts = new CancellationTokenSource();

            // resolve assemblies on device
            List<Debugger.WireProtocol.Commands.DebuggingResolveAssembly> assembliesOnDevice = await device.DebugEngine.ResolveAllAssembliesAsync(cts.Token);

            // find out which are the system assemblies
            // we will insert each system assembly in a dictionary where the key will be the assembly version
            foreach (Debugger.WireProtocol.Commands.DebuggingResolveAssembly assembly in assembliesOnDevice)
            {
                // provide feedback to the user
                await outputPaneWriter.WriteLineAsync($"Found Assembly {assembly.Result.Name} {assembly.Result.Version}.");

                // only add assemblies that are deployed
                if (assembly.Result.Status.HasFlag(Debugger.WireProtocol.Commands.DebuggingResolveAssembly.ResolvedStatus.Deployed))
                {
                    systemAssemblies.Add(assembly.Result.Name.ToLower(), assembly.Result.Version);
                }
            }

            // get the list of assemblies referenced by the project
            var referencedAssemblies = await Properties.ConfiguredProject.Services.AssemblyReferences.GetResolvedReferencesAsync();

            // build a list with the full path for each DLL and name
            List<(string path, string name)> dlls = new List<(string path, string name)>();
            foreach (IAssemblyReference reference in referencedAssemblies)
            {
                dlls.Add((await reference.GetFullPathAsync(), (await reference.GetAssemblyNameAsync()).Name));
            }

            // build a list with the PE files corresponding to each DLL
            List<(string path, string name)> pes = dlls.Select(a => (a.path.Replace(".dll", ".pe"), a.name)).ToList();

            // now we will re-deploy all system assemblies
            for (int i = 0; i < pes.Count; ++i)
            {
                string assemblyPath = pes[i].path;
                string dllPath = dlls[i].path;

                ////is this a system assembly?
                //string fileName = Path.ChangeExtension(Path.GetFileName(assemblyPath), null).ToLower();
                bool deployNewVersion = true;

                if (systemAssemblies.Contains(pes[i].name))
                {
                    // get the version of the assembly on the device 
                    Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version versionOnDevice = (Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version)systemAssemblies[pes[i].name];

                    // get the version of the assembly of the project
                    // We need to load the bytes for the assembly because other Load methods can override the path
                    // with gac or recently used paths.  This is the only way we control the exact assembly that is loaded.
                    byte[] assemblyData = null;

                    using (FileStream sr = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        assemblyData = new byte[sr.Length];
                        sr.Read(assemblyData, 0, (int)sr.Length);
                    }

                    Assembly assm = Assembly.Load(assemblyData);
                    Version versionToDeploy = assm.GetName().Version;

                    // get project properties
                    var projectConfiguration = Properties.ConfiguredProject.ProjectConfiguration;

                    // compare versions strictly, and deploy whatever assembly does not match the version on the device
                    if (versionOnDevice.Equals(versionToDeploy))
                    {
                        deployNewVersion = false;
                    }
                    else
                    {
                        //////////////////////////////////////////////// 
                        //            !!! SPECIAL CASE !!!            //
                        //                                            //
                        // MSCORLIB cannot be deployed more than once //
                        ////////////////////////////////////////////////

                        if (assm.GetName().Name.ToLower().Contains("mscorlib"))
                        {
                            await outputPaneWriter.WriteLineAsync($"Cannot deploy the base assembly '{assm.GetName().Name}', or any of his satellite assemblies, to device - {device.Description} twice. Assembly '{9}' on the device has version {versionOnDevice.MajorVersion}.{versionOnDevice.MinorVersion}.{ versionOnDevice.BuildNumber}.{ versionOnDevice.RevisionNumber}, while the program is trying to deploy version {versionToDeploy.Major}.{ versionToDeploy.Minor}.{ versionToDeploy.Build}.{ versionToDeploy.Revision} ");
                            //SetDeployFailure(message);
                            return;
                        }
                    }
                }

                // append to the deploy blob the assembly whose version does not match, or that still is not on the device
                if (deployNewVersion)
                {
                    using (FileStream fs = File.Open(assemblyPath, FileMode.Open, FileAccess.Read))
                    {
                        await outputPaneWriter.WriteLineAsync($"Adding pe file {Path.GetFileName(assemblyPath)} to deployment bundle");
                        long length = (fs.Length + 3) / 4 * 4;
                        byte[] buffer = new byte[length];

                        fs.Read(buffer, 0, (int)fs.Length);
                        assemblies.Add(buffer);
                    }
                }
            }

            await outputPaneWriter.WriteLineAsync("Deploying assemblies to device...");

            if (!await device.DebugEngine.DeploymentExecuteAsync(assemblies, false))
            {
                await outputPaneWriter.WriteLineAsync("Deploy failed.");
                return;
            }

            //
            await outputPaneWriter.WriteLineAsync("Deployment successful.");

            //}
            //else
            //{
            //    // after retry policy applied seems that we couldn't set the device in initizaled state...
            //    await outputPaneWriter.WriteLineAsync(ResourceStrings.DeviceInitializationTimeout);
            //}
        
   
        }

        public bool IsDeploySupported
        {
            get
            {
                return (ViewModelLocator?.DeviceExplorer.ConnectionStateResult == ConnectionState.Connected);
            }
        }

        public void Commit()
        {
        }

        public void Rollback()
        {
        }

        //public string[] GetDependencies(bool fIncludeStartProgram, bool fPE)
        //{
        //    string frameworkVersion = GetTargetFrameworkProperty();

        //    BuildHost buildHost

        //    IVsProjectBuildSystem innerIVsProjectBuildSystem = n
        //}

        public async Task<bool> IsDeviceInInitializeStateAsync(Engine debugEngine)
        {
            var result = await debugEngine.SetExecutionModeAsync(0, 0);

            if (result.success)
            {
                return ((result.currentExecutionMode & Debugger.WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask) == Debugger.WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Initialize);
            }
            else
            {
                return false;
            }
        }
    }
}
