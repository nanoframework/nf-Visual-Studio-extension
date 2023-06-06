////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using CliWrap;
using CliWrap.Buffered;
using GalaSoft.MvvmLight.Messaging;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class VirtualDeviceService : IVirtualDeviceService
    {
        // taken from E9007 @ nanoclr
        private const int NanoClrErrorInstanceAlreadyRunning = 9007;
        // taken from E9008 @ nanoclr
        private const int NanoClrErrorUnknowErrorStartingInstance = 9008;

        private readonly IAsyncServiceProvider _serviceProvider;
        private Process _nanoClrProcess = null;
        private INanoDeviceCommService _nanoDeviceCommService;

        /// <summary>
        /// Gets a value reporting if nanoclr tool is installed.
        /// </summary>
        public bool NanoClrIsInstalled { get; private set; }

        /// <summary>
        /// Gets a value indicating if the virtual device is running.
        /// </summary>
        /// <remarks>
        /// Reporting this is only possible if the nanoclr instance is not already running.
        /// </remarks>
        public bool VirtualDeviceIsRunning => _nanoClrProcess != null && !_nanoClrProcess.HasExited;

        /// <summary>
        /// Gets a value indicating if the virtual device can be started or stopped.
        /// </summary>
        /// <remarks>
        /// The virtual device can't be started if it's already running when this instance of Visual Studio was started.
        /// </remarks>
        public bool CanStartStopVirtualDevice { get; private set; } = true;

        public VirtualDeviceService(IAsyncServiceProvider provider)
        {
            _serviceProvider = provider;
        }

        public async System.Threading.Tasks.Task InitVirtualDeviceAsync()
        {
            await System.Threading.Tasks.Task.Run(async () =>
            {
                _nanoDeviceCommService = await _serviceProvider.GetServiceAsync(typeof(NanoDeviceCommService)) as INanoDeviceCommService;
                Assumes.Present(_nanoDeviceCommService);

                if (NanoFrameworkPackage.SettingVirtualDeviceEnable)
                {
                    // wait a little bit to allow other services to load
                    await Task.Delay(5_000);

                    // can't start/stop virtual device during install
                    CanStartStopVirtualDevice = false;

                    // take care of installing/updating nanoclr tool
                    InstallNanoClrTool();

                    if (NanoClrIsInstalled
                        && NanoFrameworkPackage.SettingVirtualDeviceAutoUpdateNanoClrImage)
                    {
                        // update nanoCLR image
                        UpdateNanoClr();
                    }

                    // start virtual device
                    await StartVirtualDeviceAsync(false);

                    if (!NanoFrameworkPackage.OptionDisableDeviceWatchers)
                    {
                        _nanoDeviceCommService.DebugClient.ReScanDevices();
                    }

                    // OK to start/stop virtual device
                    CanStartStopVirtualDevice = true;
                }

            });
        }

        public void InstallNanoClrTool()
        {
            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Install/update nanoclr tool");

            // signal install/update ongoing
            Messenger.Default.Send(new NotificationMessage(true.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);


            // get installed tool version (if installed)
            var cmd = Cli.Wrap("nanoclr")
                .WithArguments("--help")
                .WithValidation(CommandResultValidation.None);

            bool performInstallUpdate = false;

            // setup cancellation token with a timeout of 10 seconds
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                // can't start/stop virtual device during update
                CanStartStopVirtualDevice = false;

                try
                {
                    var cliResult = await cmd.ExecuteBufferedAsync(cts.Token);

                    if (cliResult.ExitCode == 0)
                    {
                        var regexResult = Regex.Match(cliResult.StandardOutput, @"(?'version'\d+\.\d+\.\d+)", RegexOptions.RightToLeft);

                        if (regexResult.Success)
                        {
                            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Running v{regexResult.Groups["version"].Value}");
                            MessageCentre.OutputVirtualDeviceMessage($"Running nanoclr v{regexResult.Groups["version"].Value}");

                            // compose version
                            Version installedVersion = new Version(regexResult.Groups[1].Value);

                            NanoClrIsInstalled = true;

                            // check latest version
                            var httpClient = new HttpClient();
                            var response = await httpClient.GetAsync("https://api.nuget.org/v3-flatcontainer/nanoclr/index.json");
                            response.EnsureSuccessStatusCode();
                            var responseContent = await response.Content.ReadAsStringAsync();
                            var package = JsonConvert.DeserializeObject<NuGetPackage>(responseContent);
                            Version latestPackageVersion = new Version(package.Versions[package.Versions.Length - 1]);

                            // check if we are running the latest one
                            if (latestPackageVersion > installedVersion)
                            {
                                // need to update
                                performInstallUpdate = true;
                            }
                        }
                        else
                        {
                            // something wrong with the output, can't proceed
                            MessageCentre.InternalErrorWriteLine("VirtualDevice: Failed to parse current nanoCLR CLI version");
                        }
                    }
                }
                catch (Win32Exception)
                {
                    // nanoclr doesn't seem to be installed
                    performInstallUpdate = true;
                    NanoClrIsInstalled = false;
                }
                finally
                {
                    // OK to start/stop virtual device
                    CanStartStopVirtualDevice = true;
                }
            });

            if (performInstallUpdate)
            {
                cmd = Cli.Wrap("dotnet")
                    .WithArguments("tool update -g nanoclr")
                    .WithValidation(CommandResultValidation.None);

                // setup cancellation token with a timeout of 1 minute
                using var cts1 = new CancellationTokenSource(TimeSpan.FromMinutes(1));

                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    // can't start/stop virtual device during install
                    CanStartStopVirtualDevice = false;

                    var cliResult = await cmd.ExecuteBufferedAsync(cts1.Token);

                    if (cliResult.ExitCode == 0)
                    {
                        var regexResult = Regex.Match(cliResult.StandardOutput, @"((?>version ')(?'version'\d+\.\d+\.\d+)(?>'))", RegexOptions.RightToLeft);

                        if (regexResult.Success)
                        {
                            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Install/update successful. Running v{regexResult.Groups["version"].Value}");
                            MessageCentre.OutputVirtualDeviceMessage($"Running nanoclr v{regexResult.Groups["version"].Value}");
                        }
                    }
                    else
                    {
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Failed to install/update nanoclr. Exit code {cliResult.ExitCode}");
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: {cliResult.StandardError}");

                        MessageCentre.OutputVirtualDeviceMessage($"ERROR: failed to install/update nanoclr. Exit code {cliResult.ExitCode}");

                        NanoClrIsInstalled = false;
                    }

                    // OK to start/stop virtual device
                    CanStartStopVirtualDevice = true;
                });
            }

            // signal install/update completed
            Messenger.Default.Send(new NotificationMessage(false.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);
        }

        public void UpdateNanoClr()
        {
            var cmd = Cli.Wrap("nanoclr")
                .WithArguments("instance --update")
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 1 minute
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                // can't start/stop virtual device during install
                CanStartStopVirtualDevice = false;

                var cliResult = await cmd.ExecuteBufferedAsync(cts.Token);

                if (cliResult.ExitCode == 0)
                {
                    var regexResult = Regex.Match(cliResult.StandardOutput, @"((?>Updated to v)(?'version'\d+\.\d+\.\d+\.\d+))");

                    if (regexResult.Success)
                    {
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: updated nanoCLR image to v{regexResult.Groups["version"].Value}");
                        MessageCentre.OutputVirtualDeviceMessage($"Updated nanoCLR image to v{regexResult.Groups["version"].Value}");
                    }
                }
                else
                {
                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: failed to update the nanoCLR image");
                    MessageCentre.OutputVirtualDeviceMessage("ERROR: failed to update the nanoCLR image");
                }

                // OK to start/stop virtual device
                CanStartStopVirtualDevice = true;
            });
        }
        public string ListVirtualSerialPorts()
        {
            var cmd = Cli.Wrap("nanoclr")
                        .WithArguments("virtualserial --list -v q")
                        .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 1 minute
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            return ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                var cliResult = await cmd.ExecuteBufferedAsync(cts.Token);

                if (cliResult.ExitCode == 0)
                {
                    return cliResult.StandardOutput;
                }
                else
                {
                    return "";
                }
            });
        }

        public bool CreateVirtualSerialPort(string portName, out string executionLog)
        {
            var cmd = Cli.Wrap("nanoclr")
                .WithArguments($"virtualserial --create {portName} -v q")
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 1 minute
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var cliResult = ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                return await cmd.ExecuteBufferedAsync(cts.Token);
            });

            executionLog = cliResult.StandardOutput;

            return (cliResult.ExitCode == 0);
        }

        public void StopVirtualDevice(bool shutdownProcessing = false)
        {
            if (_nanoClrProcess != null)
            {
                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);

                    byte[] ctrlC = new byte[] { 0x03 };

                    if (shutdownProcessing)
                    {
                        // call from VS shutdown handler
                        // just kill process

                        try
                        {
                            _nanoClrProcess.EnableRaisingEvents = false;
                            _nanoClrProcess.Exited -= VirtualDeviceProcess_Exited;
                            _nanoClrProcess.OutputDataReceived -= nanoClrProcess_OutputDataReceived;

                            // send Ctrl+C escape
                            _nanoClrProcess.StandardInput.Write(ctrlC);

                            // pause for a moment 
                            await Task.Delay(250);

                            // kill process
                            _nanoClrProcess.Kill();
                        }
                        catch
                        {
                            // no need to process anything as VS is exiting
                        }
                    }
                    else
                    {
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Attempting to stop virtual device");

                        try
                        {
                            _nanoClrProcess.EnableRaisingEvents = false; 
                            _nanoClrProcess.OutputDataReceived -= nanoClrProcess_OutputDataReceived;
                            _nanoClrProcess.Exited -= VirtualDeviceProcess_Exited;

                            // send Ctrl+C escape
                            _nanoClrProcess.StandardInput.Write(ctrlC);

                            // pause for a moment 
                            await Task.Delay(250);

                            // kill process
                            _nanoClrProcess.Kill();

                            // output message to pane
                            MessageCentre.OutputVirtualDeviceMessage("");
                            MessageCentre.OutputVirtualDeviceMessage("");
                            MessageCentre.OutputVirtualDeviceMessage("**********************************");
                            MessageCentre.OutputVirtualDeviceMessage("*** Virtual nanoDevice stopped ***");
                            MessageCentre.OutputVirtualDeviceMessage("**********************************");
                            MessageCentre.OutputVirtualDeviceMessage("");
                            MessageCentre.OutputVirtualDeviceMessage("");

                            // rescan devices
                            _nanoDeviceCommService.DebugClient.ReScanDevices();
                        }
                        catch
                        {
                            // catch all, don't bother
                        }

                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Virtual device stopped");

                        _nanoClrProcess = null;
                    }
                });
            }
            else
            {
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Attempting to stop virtual device without it being started");
            }
        }

        public async Task<bool> StartVirtualDeviceAsync(bool rescanDevices)
        {
            bool firstPassRunning = true;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);

            // signal start operation
            Messenger.Default.Send(new NotificationMessage(true.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);

            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Attempting to start virtual device");

            if (_nanoClrProcess != null)
            {
                // this shouldn't happen, still...

                _nanoClrProcess.OutputDataReceived -= nanoClrProcess_OutputDataReceived;
                _nanoClrProcess.Exited -= VirtualDeviceProcess_Exited;

                _nanoClrProcess.Kill();

                _nanoClrProcess = null;
            }

            try
            {
                // check virtual serial ports
                var listOfVirtualSerialPorts = ListVirtualSerialPorts();

                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Virtual Serial Ports listed");

                if (listOfVirtualSerialPorts.Contains("No virtual serial port pairs found.")
                    || !listOfVirtualSerialPorts.Contains(NanoFrameworkPackage.SettingVirtualDevicePort))
                {
                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: Creating Virtual Serial Port");

                    // no virtual ports installed or the specified COM port is not there
                    var executionResult = CreateVirtualSerialPort(NanoFrameworkPackage.SettingVirtualDevicePort, out string executionLog);

                    var regexResult = Regex.Match(executionLog, "(?>Creating a new Virtual Bridge )(?'comport'COM\\d{1,3})(?>:COM\\d{1,3})");
                    if (!executionResult || !regexResult.Success)
                    {
                        // failed to create virtual device

                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Failed to create Virtual Serial Port");

                        // report this and suggest an alternative
                        MessageCentre.OutputVirtualDeviceMessage("**********************************************");
                        MessageCentre.OutputVirtualDeviceMessage("*** Failed to create Virtual Serial Device ***");
                        MessageCentre.OutputVirtualDeviceMessage("**********************************************");
                        MessageCentre.OutputVirtualDeviceMessage("");
                        MessageCentre.OutputVirtualDeviceMessage($"Please run this at a command prompt: 'nanoclr --virtualserial --create {regexResult.Groups["comport"].Value}'");
                        MessageCentre.OutputVirtualDeviceMessage($"Then update the Serial Port input in the Virtual Device tab in the Settings dialog with {regexResult.Groups["comport"].Value}");
                        MessageCentre.OutputVirtualDeviceMessage("");

                        // done here
                        return false;
                    }

                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: Virtual Serial Port created successfully");

                    // store serial port name
                    NanoFrameworkPackage.SettingVirtualDevicePort = regexResult.Groups["comport"].Value;
                }

                // sanity check for empty port name
                if (string.IsNullOrEmpty(NanoFrameworkPackage.SettingVirtualDevicePort))
                {
                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: Storing Virtual Serial Port information in settings");

                    var regexResult = Regex.Match(listOfVirtualSerialPorts, @"(?'comport'COM\d{1,3})(?><->COM\d{1,3})");
                    NanoFrameworkPackage.SettingVirtualDevicePort = regexResult.Groups["comport"].Value;
                }

                // check if we are to load a local nanoCLR instance
                string nanoClrInstanceOption = string.Empty;

                if(NanoFrameworkPackage.SettingLoadNanoClrInstance && !string.IsNullOrEmpty(NanoFrameworkPackage.SettingPathOfLocalNanoClrInstance))
                {
                    nanoClrInstanceOption = $"--localinstance \"{NanoFrameworkPackage.SettingPathOfLocalNanoClrInstance}\"";
                }

                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Setting up process to run virtual device");

                // OK to launch process with nanoclr
                _nanoClrProcess = new Process();

                _nanoClrProcess.StartInfo.FileName = "nanoclr";
                _nanoClrProcess.StartInfo.Arguments = $"run --serialport {NanoFrameworkPackage.SettingVirtualDevicePort} {nanoClrInstanceOption} --waitfordebugger --loopafterexit --monitorparentpid {Process.GetCurrentProcess().Id}";
                _nanoClrProcess.StartInfo.UseShellExecute = false;
                _nanoClrProcess.StartInfo.CreateNoWindow = true;
                _nanoClrProcess.StartInfo.RedirectStandardOutput = true;
                _nanoClrProcess.StartInfo.RedirectStandardInput = true;
                _nanoClrProcess.StartInfo.RedirectStandardError = true;
                _nanoClrProcess.OutputDataReceived += nanoClrProcess_OutputDataReceived;
                _nanoClrProcess.EnableRaisingEvents = true;

                // enumeration is completed, OK to start virtual device
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Starting process for virtual device");

            startProces:
                if (_nanoClrProcess.Start())
                {
                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: Process started");

                    // can only begin read if not already done
                    if (firstPassRunning)
                    {
                        _nanoClrProcess.BeginOutputReadLine();
                    }

                    if (_nanoClrProcess.WaitForExit(1_000))
                    {
                        // something went wrong starting the process
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Process exited prematurely");

                        if (firstPassRunning)
                        {
                            // lower flag
                            firstPassRunning = false;

                            Thread.Sleep(5_000);

                            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Retrying to start process");

                            goto startProces;
                        }

                        // this will go to failedToStart
                    }
                    else
                    {
                        // all good!

                        // set handler for process exited
                        _nanoClrProcess.Exited += VirtualDeviceProcess_Exited;

                        // done here
                        return true;
                    }
                }
                else
                {
                    // this will go to failedToStart
                }

                // failedToStart:
                // hasn't started
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Failed to start. Exit code was: {_nanoClrProcess.ExitCode}");

                // check if this is failing because the serial port it's taken
                if (_nanoClrProcess.ExitCode == NanoClrErrorInstanceAlreadyRunning
                    || _nanoClrProcess.ExitCode == NanoClrErrorUnknowErrorStartingInstance)
                {
                    MessageCentre.OutputVirtualDeviceMessage("");
                    MessageCentre.OutputVirtualDeviceMessage("***********************************************************************************");
                    MessageCentre.OutputVirtualDeviceMessage("Failed to start virtual device. Possibly there is another instance already running.");
                    MessageCentre.OutputVirtualDeviceMessage("No output from that instance will be shown here.");
                    MessageCentre.OutputVirtualDeviceMessage("***********************************************************************************");
                    MessageCentre.OutputVirtualDeviceMessage("");

                }
                else
                {
                    MessageCentre.OutputVirtualDeviceMessage("");
                    MessageCentre.OutputVirtualDeviceMessage("*****************************************************");
                    MessageCentre.OutputVirtualDeviceMessage($"Failed to start virtual device. Exit code was: {_nanoClrProcess.ExitCode}");
                    MessageCentre.OutputVirtualDeviceMessage("*****************************************************");
                    MessageCentre.OutputVirtualDeviceMessage("");
                }
            }
            catch (Exception ex)
            {
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Failed to start. Exception was: {ex.Message}");
                MessageCentre.OutputVirtualDeviceMessage("");
                MessageCentre.OutputVirtualDeviceMessage("**************************************************");
                MessageCentre.OutputVirtualDeviceMessage($"Failed to start virtual device. Exception was: {ex.Message}");
                MessageCentre.OutputVirtualDeviceMessage("**************************************************");
                MessageCentre.OutputVirtualDeviceMessage("");
            }
            finally
            {
                // signal start operation completed
                Messenger.Default.Send(new NotificationMessage(false.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);

                // rescan devices, if start was successful and this wasn't requested to skip
                if (_nanoClrProcess != null
                    && !_nanoClrProcess.HasExited
                    && rescanDevices)
                {
                    _nanoDeviceCommService.DebugClient.ReScanDevices();
                }
            }

            // null process
            _nanoClrProcess = null;

            // done here
            return false;
        }

        private void VirtualDeviceProcess_Exited(object sender, EventArgs e)
        {
            _nanoClrProcess.OutputDataReceived -= nanoClrProcess_OutputDataReceived;
            _nanoClrProcess.Exited -= VirtualDeviceProcess_Exited;

            _nanoClrProcess = null;
        }

        private void nanoClrProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                MessageCentre.OutputVirtualDeviceMessage(e.Data);
            }
        }

        internal class NuGetPackage
        {
            public string[] Versions { get; set; }
        }
    }
}
