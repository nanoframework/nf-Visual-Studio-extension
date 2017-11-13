//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.References;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.Practices.ServiceLocation;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using Microsoft.VisualStudio.Shell.Interop;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.TextManager.Interop;

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

        //// TODO: Specify the assembly full name here
        //[ExportPropertyXamlRuleDefinition("nanoFramework.Tools.VS2017.Extension, Version=0.2.4.0, Culture=neutral, PublicKeyToken=9be6e469bc4921f1", "XamlRuleToCode:NanoDebugger.xaml", "Project")]
        //[AppliesTo(NanoCSharpProjectUnconfigured.UniqueCapability)]
        //private object DebuggerXaml { get { throw new NotImplementedException(); } }
        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        [Import]
        IProjectService ProjectService { get; set; }

        //public override async Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        //{
        //    var properties = await this.DebuggerProperties.GetNanoDebuggerPropertiesAsync();
        //    string commandValue = await properties.NanoDebuggerCommand.GetEvaluatedValueAtEndAsync();
        //    return !string.IsNullOrEmpty(commandValue);
        //}

        //public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        //{
        //    var settings = new DebugLaunchSettings(launchOptions);

        //    // The properties that are available via DebuggerProperties are determined by the property XAML files in your project.
        //    var debuggerProperties = await this.DebuggerProperties.GetNanoDebuggerPropertiesAsync();
        //    settings.CurrentDirectory = await debuggerProperties.NanoDebuggerWorkingDirectory.GetEvaluatedValueAtEndAsync();
        //    settings.Executable = await debuggerProperties.NanoDebuggerCommand.GetEvaluatedValueAtEndAsync();
        //    settings.Arguments = await debuggerProperties.NanoDebuggerCommandArguments.GetEvaluatedValueAtEndAsync();
        //    settings.LaunchOperation = DebugLaunchOperation.CreateProcess;

        //    // TODO: Specify the right debugger engine
        //    settings.LaunchDebugEngineGuid = DebuggerEngines.ManagedOnlyEngine;

        //    return new IDebugLaunchSettings[] { settings };
        //}

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        {
            return TplExtensions.TrueTask;
        }

        public override async Task LaunchAsync(DebugLaunchOptions launchOptions)
        {
            var targets = await QueryDebugTargetsAsync(launchOptions).ConfigureAwait(true);

            var hr =  await DoLaunchAsync(targets.ToArray()).ConfigureAwait(true);



           // return base.LaunchAsync(await CreateLaunchSettingsAsync(launchOptions));
        }

        /// <summary>
        /// Launches the Visual Studio debugger.
        /// </summary>
        //protected Task<IReadOnlyList<VsDebugTargetProcessInfo>> DoLaunchAsync(params IDebugLaunchSettings[] launchSettings)
        protected Task<int> DoLaunchAsync(params IDebugLaunchSettings[] launchSettings)
        {
            //VsDebugTargetInfo4[] launchSettingsNative = launchSettings.Select(GetDebuggerStruct4).ToArray();
            //if (launchSettingsNative.Length == 0)
            //{
            //    return Task.FromResult<IReadOnlyList<VsDebugTargetProcessInfo>>(new VsDebugTargetProcessInfo[0]);
            //}


            var debuggerProperties = this.Properties.GetNanoDebuggerPropertiesAsync().Result;
            var currentDirectory = debuggerProperties.NanoDebuggerWorkingDirectory.GetEvaluatedValueAtEndAsync().Result;

            string commandLine = launchSettings[0].Arguments;

            var deployDeviceName = ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().SelectedDevice.Description;
            var portName = ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().SelectedTransportType.ToString();

            commandLine = string.Format("{0} \"{1}{2}\"", commandLine, CorDebugProcess.c_DeployDeviceName, deployDeviceName);


            VsDebugTargetInfo2[] launchSettingsNative = launchSettings.Select(GetDebuggerStruct2).ToArray();
            if (launchSettingsNative.Length == 0)
            {
                //return Task.FromResult<IReadOnlyList<VsDebugTargetProcessInfo>>(new VsDebugTargetProcessInfo[0]);
            }
            unsafe
            {
                VsDebugTargetInfo2 vsDebugTargetInfo = new VsDebugTargetInfo2();

                vsDebugTargetInfo.bstrArg = commandLine;
                vsDebugTargetInfo.bstrCurDir = currentDirectory;
                vsDebugTargetInfo.bstrEnv = null;
                vsDebugTargetInfo.bstrExe = typeof(CorDebugProcess).Assembly.Location;
                vsDebugTargetInfo.bstrOptions = null;
                vsDebugTargetInfo.bstrPortName = portName;
                vsDebugTargetInfo.bstrRemoteMachine = null;
                vsDebugTargetInfo.cbSize = (uint)Marshal.SizeOf(vsDebugTargetInfo);
                vsDebugTargetInfo.dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
                vsDebugTargetInfo.dwDebugEngineCount = 0;
                vsDebugTargetInfo.dwProcessId = 0;
                vsDebugTargetInfo.dwReserved = 0;
                vsDebugTargetInfo.fSendToOutputWindow = 0;
                vsDebugTargetInfo.guidLaunchDebugEngine = CorDebug.EngineGuid;
                vsDebugTargetInfo.guidPortSupplier = DebugPortSupplier.PortSupplierGuid;
                vsDebugTargetInfo.guidProcessLanguage = Guid.Empty;
                vsDebugTargetInfo.hStdError = 0;
                vsDebugTargetInfo.hStdInput = 0;
                vsDebugTargetInfo.hStdOutput = 0;
                vsDebugTargetInfo.LaunchFlags = 0;
                vsDebugTargetInfo.pDebugEngines = IntPtr.Zero;
                vsDebugTargetInfo.pUnknown = null;



                //VsDebugTargetInfo2 vsDebugTargetInfo = launchSettingsNative[0];
                byte* bpDebugTargetInfo = stackalloc byte[(int)vsDebugTargetInfo.cbSize];
                IntPtr ipDebugTargetInfo = (IntPtr)bpDebugTargetInfo;
                Marshal.StructureToPtr(vsDebugTargetInfo, ipDebugTargetInfo, false);

                try
                {
                    //var shellDebugger = ServiceProvider.GetService(typeof(SVsShellDebugger)) as IVsDebugger4;
                    //var launchResults = new VsDebugTargetProcessInfo[launchSettingsNative.Length];
                    //shellDebugger.LaunchDebugTargets4((uint)launchSettingsNative.Length, launchSettingsNative, launchResults);
                    //return Task.FromResult<IReadOnlyList<VsDebugTargetProcessInfo>>(launchResults);


                    var shellDebugger = ServiceProvider.GetService(typeof(SVsShellDebugger)) as IVsDebugger2;

                    IVsEnumGUID ppEEnum;
                    var result =  shellDebugger.EnumDebugEngines(out ppEEnum);

                    Guid[] e0 = new Guid[1];
                    uint pf;
                    ppEEnum.Next(1, e0, out pf);

                    string name = "";
                    shellDebugger.GetEngineName(ref e0[0], out name);



                    var shellDebugger4 = ServiceProvider.GetService(typeof(SVsShellDebugger)) as IVsDebugger4;

                    


                    var hr = shellDebugger.LaunchDebugTargets2(1, ipDebugTargetInfo);



                    return Task.FromResult<int>(shellDebugger.LaunchDebugTargets2(1, ipDebugTargetInfo));


                }
                catch (Exception ex)
                {
                    return null;
                }
                finally
                {
                    // Free up the memory allocated to the (mostly) managed debugger structure.
                    //foreach (var nativeStruct in launchSettingsNative)
                    //{
                    //    FreeVsDebugTargetInfoStruct(nativeStruct);
                    //}
                    if (ipDebugTargetInfo != null)
                        Marshal.DestroyStructure(ipDebugTargetInfo, launchSettingsNative[0].GetType());

                }
            }
        }


        /// <summary>
        /// Copy information over from the contract struct to the native one.
        /// </summary>
        /// <returns>The native struct.</returns>
        internal static VsDebugTargetInfo4 GetDebuggerStruct4(IDebugLaunchSettings info)
        {
            var debugInfo = new VsDebugTargetInfo4();

            // **Begin common section -- keep this in sync with GetDebuggerStruct**
            debugInfo.dlo = (uint)info.LaunchOperation;
            debugInfo.LaunchFlags = (uint)info.LaunchOptions;
            debugInfo.bstrRemoteMachine = info.RemoteMachine;
            debugInfo.bstrArg = info.Arguments;
            debugInfo.bstrCurDir = info.CurrentDirectory;
            debugInfo.bstrExe = info.Executable;

            debugInfo.bstrEnv = GetSerializedEnvironmentString(info.Environment);
            debugInfo.guidLaunchDebugEngine = info.LaunchDebugEngineGuid;

            List<Guid> guids = new List<Guid>(1);
            guids.Add(info.LaunchDebugEngineGuid);
            if (info.AdditionalDebugEngines != null)
            {
                guids.AddRange(info.AdditionalDebugEngines);
            }

            debugInfo.dwDebugEngineCount = (uint)guids.Count;

            byte[] guidBytes = GetGuidBytes(guids);
            debugInfo.pDebugEngines = Marshal.AllocCoTaskMem(guidBytes.Length);
            Marshal.Copy(guidBytes, 0, debugInfo.pDebugEngines, guidBytes.Length);

            debugInfo.guidPortSupplier = info.PortSupplierGuid;
            debugInfo.bstrPortName = info.PortName;
            debugInfo.bstrOptions = info.Options;
            debugInfo.fSendToOutputWindow = info.SendToOutputWindow ? 1 : 0;
            debugInfo.dwProcessId = unchecked((uint)info.ProcessId);
            debugInfo.pUnknown = info.Unknown;
            debugInfo.guidProcessLanguage = info.ProcessLanguageGuid;

            // **End common section**

            if (info.StandardErrorHandle != IntPtr.Zero || info.StandardInputHandle != IntPtr.Zero || info.StandardOutputHandle != IntPtr.Zero)
            {
                VsDebugStartupInfo processStartupInfo = new VsDebugStartupInfo
                {
                    hStdInput = unchecked((uint)info.StandardInputHandle.ToInt32()),
                    hStdOutput = unchecked((uint)info.StandardOutputHandle.ToInt32()),
                    hStdError = unchecked((uint)info.StandardErrorHandle.ToInt32()),
                    flags = (uint)__DSI_FLAGS.DSI_USESTDHANDLES,
                };
                debugInfo.pStartupInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf(processStartupInfo));
                Marshal.StructureToPtr(processStartupInfo, debugInfo.pStartupInfo, false);
            }

            debugInfo.AppPackageLaunchInfo = info.AppPackageLaunchInfo;
            debugInfo.project = info.Project;

            return debugInfo;
        }

        /// <summary>
        /// Copy information over from the contract struct to the native one.
        /// </summary>
        /// <returns>The native struct.</returns>
        internal static VsDebugTargetInfo2 GetDebuggerStruct2(IDebugLaunchSettings info)
        {
            var debugInfo = new VsDebugTargetInfo2();

            // **Begin common section -- keep this in sync with GetDebuggerStruct**
            debugInfo.dlo = (uint)info.LaunchOperation;
            debugInfo.LaunchFlags = (uint)info.LaunchOptions;
            debugInfo.bstrRemoteMachine = info.RemoteMachine;
            debugInfo.bstrArg = info.Arguments;
            debugInfo.bstrCurDir = info.CurrentDirectory;
            debugInfo.bstrExe = info.Executable;

            debugInfo.bstrEnv = GetSerializedEnvironmentString(info.Environment);
            debugInfo.guidLaunchDebugEngine = info.LaunchDebugEngineGuid;

            List<Guid> guids = new List<Guid>(1);
            guids.Add(info.LaunchDebugEngineGuid);
            if (info.AdditionalDebugEngines != null)
            {
                guids.AddRange(info.AdditionalDebugEngines);
            }

            debugInfo.dwDebugEngineCount = (uint)guids.Count;

            byte[] guidBytes = GetGuidBytes(guids);
            debugInfo.pDebugEngines = Marshal.AllocCoTaskMem(guidBytes.Length);
            Marshal.Copy(guidBytes, 0, debugInfo.pDebugEngines, guidBytes.Length);

            debugInfo.guidPortSupplier = info.PortSupplierGuid;
            debugInfo.bstrPortName = info.PortName;
            debugInfo.bstrOptions = info.Options;
            debugInfo.fSendToOutputWindow = info.SendToOutputWindow ? 1 : 0;
            debugInfo.dwProcessId = unchecked((uint)info.ProcessId);
            debugInfo.pUnknown = info.Unknown;
            debugInfo.guidProcessLanguage = info.ProcessLanguageGuid;

            // **End common section**

            //if (info.StandardErrorHandle != IntPtr.Zero || info.StandardInputHandle != IntPtr.Zero || info.StandardOutputHandle != IntPtr.Zero)
            //{
            //    VsDebugStartupInfo processStartupInfo = new VsDebugStartupInfo
            //    {
            //        hStdInput = unchecked((uint)info.StandardInputHandle.ToInt32()),
            //        hStdOutput = unchecked((uint)info.StandardOutputHandle.ToInt32()),
            //        hStdError = unchecked((uint)info.StandardErrorHandle.ToInt32()),
            //        flags = (uint)__DSI_FLAGS.DSI_USESTDHANDLES,
            //    };
            //    debugInfo.pStartupInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf(processStartupInfo));
            //    Marshal.StructureToPtr(processStartupInfo, debugInfo.pStartupInfo, false);
            //}

            //debugInfo.AppPackageLaunchInfo = info.AppPackageLaunchInfo;
            //debugInfo.project = info.Project;

            return debugInfo;
        }


        /// <summary>
        /// Collects an array of GUIDs into an array of bytes.
        /// </summary>
        /// <remarks>
        /// The order of the GUIDs are preserved, and each GUID is copied exactly one after the other in the byte array.
        /// </remarks>
        private static byte[] GetGuidBytes(IList<Guid> guids)
        {
            int sizeOfGuid = Guid.Empty.ToByteArray().Length;
            byte[] bytes = new byte[guids.Count * sizeOfGuid];
            for (int i = 0; i < guids.Count; i++)
            {
                byte[] guidBytes = guids[i].ToByteArray();
                guidBytes.CopyTo(bytes, i * sizeOfGuid);
            }

            return bytes;
        }

        /// <summary>
        /// Frees memory allocated by GetDebuggerStruct.
        /// </summary>
        internal static void FreeVsDebugTargetInfoStruct(VsDebugTargetInfo4 nativeStruct)
        {
            if (nativeStruct.pDebugEngines != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(nativeStruct.pDebugEngines);
            }

            if (nativeStruct.pStartupInfo != IntPtr.Zero)
            {
                Marshal.DestroyStructure(nativeStruct.pStartupInfo, typeof(VsDebugStartupInfo));
                Marshal.FreeCoTaskMem(nativeStruct.pStartupInfo);
            }
        }

        /// <summary>
        /// Converts the environment key value pairs to a valid environment string of the form
        /// key=value/0key2=value2/0/0, with null's between each entry and a double null terminator.
        /// </summary>
        private static string GetSerializedEnvironmentString(IDictionary<string, string> environment)
        {
            // If no dictionary was set, or its empty, the debugger wants null for its environment block.
            if (environment == null || environment.Count == 0)
            {
                return null;
            }

            // Collect all the variables as a null delimited list of key=value pairs.
            StringBuilder result = new StringBuilder();
            foreach (var pair in environment)
            {
                result.Append(pair.Key);
                result.Append('=');
                result.Append(pair.Value);
                result.Append('\0');
            }

            // Add a final list-terminating null character.
            // This is sent to native code as a BSTR and no null is added automatically.
            // But the contract of the format of the data requires that this be a null-delimited,
            // null-terminated list.
            result.Append('\0');
            return result.ToString();
        }

        public async override Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            var result = new List<IDebugLaunchSettings>();

            result.Add(await CreateLaunchSettingsAsync(launchOptions));

            return result;
        }

        private async Task<DebugLaunchSettings> CreateLaunchSettingsAsync(DebugLaunchOptions options)
        {
            string commandLine = await GetCommandLineForLaunchAsync();

            var deployDeviceName = ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().SelectedDevice.Description;
            var portName = ServiceLocator.Current.GetInstance<DeviceExplorerViewModel>().SelectedTransportType.ToString();

            commandLine = string.Format("{0} \"{1}{2}\"", commandLine, CorDebugProcess.c_DeployDeviceName, deployDeviceName);

            var debuggerProperties = await this.Properties.GetNanoDebuggerPropertiesAsync();

            var settings = new DebugLaunchSettings(options)
            {
                CurrentDirectory = await debuggerProperties.NanoDebuggerWorkingDirectory.GetEvaluatedValueAtEndAsync(),
                LaunchOperation = DebugLaunchOperation.CreateProcess,
                LaunchDebugEngineGuid = CorDebug.EngineGuid,//DebuggerGuids.EngineId,// CorDebug.EngineGuid,
                Project = VsHierarchy,
                PortSupplierGuid = DebugPortSupplier.PortSupplierGuid, //NanoDebugPortSupplier.PortSupplierGuid,
                PortName = portName,//deployDeviceName,
                ProcessLanguageGuid = Guid.Empty,
                Executable = typeof(CorDebugProcess).Assembly.Location,
                Arguments = commandLine,
                ProcessId = 0,
                Options = null,
                RemoteMachine = null,
                Unknown = null,
                SendToOutputWindow = false,
                StandardErrorHandle = IntPtr.Zero,
                StandardInputHandle = IntPtr.Zero,
                StandardOutputHandle = IntPtr.Zero
            };

            //var props = await new Rules.RuleProperties(ConfiguredProject).GetPythonDebugLaunchProviderPropertiesAsync().ConfigureAwait(false);

            //settings.Executable = await props.LocalDebuggerCommand.GetEvaluatedValueAtEndAsync().ConfigureAwait(false);
            //settings.Arguments = await props.LocalDebuggerCommandArguments.GetEvaluatedValueAtEndAsync().ConfigureAwait(false);
            //settings.CurrentDirectory = await props.LocalDebuggerWorkingDirectory.GetEvaluatedValueAtEndAsync().ConfigureAwait(false);

            //var envString = await props.LocalDebuggerEnvironment.GetEvaluatedValueAtEndAsync().ConfigureAwait(false);

            //if (!string.IsNullOrEmpty(envString))
            //{
            //    var mergeEnv = await props.LocalDebuggerMergeEnvironment.GetEvaluatedValueAtEndAsync().ConfigureAwait(false);
            //    if ("true".Equals(mergeEnv ?? "", StringComparison.OrdinalIgnoreCase))
            //    {
            //        FillFromCurrentEnvironment(settings.Environment);
            //    }
            //    ParseEnvironment(settings.Environment, envString);
            //}

            return settings;
        }

        private async Task<string> GetCommandLineForLaunchAsync()
        {
            CommandLineBuilder cb = new CommandLineBuilder();

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

            foreach (string assembly in assemblyList)
            {
                cb.AddArguments("/load:" + assembly);
            }

            string commandLine = cb.ToString();
            commandLine = Environment.ExpandEnvironmentVariables(commandLine);

            return commandLine;
        }
    }
}
