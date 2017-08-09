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
        private int assemblyList;

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

        [Import]
        IProjectService ProjectService { get; set; }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter outputPaneWriter)
        {
            // make sure we're on the UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // just in case....
            if ((ViewModelLocator?.DeviceExplorer.ConnectionStateResult != ConnectionState.Connected))
            {
                // can't debug
                // throw exception to signal deployment failure
                throw new Exception("There is no device connected. Check Device Explorer tool window.");
            }

            // get the device here so we are not always carrying the full path to the device
            NanoDeviceBase device = ViewModelLocator.DeviceExplorer.SelectedDevice;

            // user feedback
            await outputPaneWriter.WriteLineAsync($"Getting things ready to deploy assemblies to nanoFramework device: {device.Description}.");

            List<byte[]> assemblies = new List<byte[]>();

            // build policy to check for device initialized
            // on false result
            // set retry count
            // set wait time between retries
            var deviceInInitStatePolicy = Policy.HandleResult<bool>(r => r == false).WaitAndRetryAsync(
                _numberOfRetries,
                retryAttempt => TimeSpan.FromMilliseconds(_timeoutMiliseconds),
                async (result, timeSpan, retryCount, context) =>
                {
                    // provide feedback to user on the 1st pass
                    if (retryCount == 0)
                    {
                        await outputPaneWriter.WriteLineAsync(ResourceStrings.WaitingDeviceInitialization);
                    }
                });

            // check if device is still in initialized state
            if (await device.DebugEngine.IsDeviceInInitializeStateAsync())
            {
                // device is still in initialization state, try resume execution
                await device.DebugEngine.ResumeExecutionAsync();
            }

            if (!await deviceInInitStatePolicy.ExecuteAsync(async () => { return await device.DebugEngine.IsDeviceInInitializeStateAsync(); }))
            { 
                // device is NOT in initialization state, meaning is running or stopped
                await outputPaneWriter.WriteLineAsync(ResourceStrings.DeviceInitialized);

                ///////////////////////////////////////////////////////
                // get the list of assemblies referenced by the project
                var referencedAssemblies = await Properties.ConfiguredProject.Services.AssemblyReferences.GetResolvedReferencesAsync();

                //////////////////////////////////////////////////////////////////////////
                // get the list of other projects referenced by the project being deployed
                var referencedProjects = await Properties.ConfiguredProject.Services.ProjectReferences.GetResolvedReferencesAsync();

                /////////////////////////////////////////////////////////
                // get the target path to reach the PE for the executable

                // this is not currently working so...
                //var props = Properties.GetConfigurationGeneralPropertiesAsync();

                //... we need to access the target path using reflexion (step by step)
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

                // Keep track of total assembly size
                long totalSizeOfAssemblies = 0;

                // now we will re-deploy all system assemblies
                foreach(string pePath in peCollection)
                {
                    // append to the deploy blob the assembly
                    using (FileStream fs = File.Open(pePath, FileMode.Open, FileAccess.Read))
                    {
                        long length = (fs.Length + 3) / 4 * 4;
                        await outputPaneWriter.WriteLineAsync($"Adding pe file {Path.GetFileNameWithoutExtension(pePath)} to deployment bundle, size in bytes {length.ToString()}");
                        byte[] buffer = new byte[length];

                        fs.Read(buffer, 0, (int)fs.Length);
                        assemblies.Add(buffer);

                        // Increment totalizer
                        totalSizeOfAssemblies += length;
                    }
                }

                await outputPaneWriter.WriteLineAsync($"Deploying assemblies to device...total size in bytes {totalSizeOfAssemblies.ToString()}.");

                if (!await device.DebugEngine.DeploymentExecuteAsync(assemblies, false))
                {
                    // throw exception to signal deployment failure
                    throw new Exception("Deploy failed.");
                }

                // deployment successfull
                await outputPaneWriter.WriteLineAsync("Deployment successful.");

                // reboot device so the assemblies get loaded
                try
                {
                    await device.DebugEngine.RebootDeviceAsync();
                }
                catch { }

                // reset the hash for the connected device so the deployment information can be refreshed
                ViewModelLocator.DeviceExplorer.LastDeviceConnectedHash = 0;
            }
            else
            {
                // after retry policy applied seems that we couldn't set the device in initizaled state...
                // throw exception to signal deployment failure
                throw new Exception(ResourceStrings.DeviceInitializationTimeout);
            }
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
    }
}
