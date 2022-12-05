////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using CliWrap;
using CliWrap.Buffered;
using GalaSoft.MvvmLight.Messaging;
using Humanizer;
using Microsoft;
using Microsoft.VisualStudio.RpcContracts.Commands;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Mono.Cecil.Cil;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Packaging;
using System.Management.Instrumentation;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class VirtualDeviceService : IVirtualDeviceService
    {
        private readonly Microsoft.VisualStudio.Shell.IAsyncServiceProvider _serviceProvider;
        private Process _nanoClrProcess = null;
        private INanoDeviceCommService _nanoDeviceCommService;

        public bool NanoClrInstalled { get; private set; }
        public bool VirtualDeviceIsRunning => _nanoClrProcess != null && !_nanoClrProcess.HasExited;

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
                    // take care of installing/updating nanoclr tool
                    InstallNanoClrTool();

                    if (NanoFrameworkPackage.SettingVirtualDeviceAutoUpdateNanoClrImage)
                    {
                        // update nanoCLR image
                        UpdateNanoClr();
                    }

                    // start virtual device
                    await StartVirtualDeviceAsync(false);
                }

                if (!NanoFrameworkPackage.OptionDisableDeviceWatchers)
                {
                    _nanoDeviceCommService.DebugClient.StartDeviceWatchers();
                }
               
            });
        }

        public void InstallNanoClrTool()
        {
            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Install/upate nanoclr tool");

            var cmd = Cli.Wrap("dotnet")
                .WithArguments("tool update -g nanoclr")
                .WithValidation(CommandResultValidation.None);

            // signal install/update ongoing
            Messenger.Default.Send(new NotificationMessage(true.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);

            // setup cancellation token with a timeout of 1 minute
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromMinutes(1));

                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    var cliResult = await cmd.ExecuteBufferedAsync(cts.Token);

                    if (cliResult.ExitCode == 0)
                    {
                        var regexResult = Regex.Match(cliResult.StandardOutput, @"((?>\(version ')(?'version'\d+\.\d+\.\d+)(?>'\)))");

                        if (regexResult.Success)
                        {
                            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Install/update successful v{regexResult.Groups["version"].Value}");
                            MessageCentre.OutputVirtualDeviceMessage($"Running nanoclr v{regexResult.Groups["version"].Value}");
                        }

                        NanoClrInstalled = true;
                    }
                    else
                    {
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Failed to install/update nanoclr. Exit code {cliResult.ExitCode}");
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: {cliResult.StandardError}");

                        MessageCentre.OutputVirtualDeviceMessage($"ERROR: failed to install/update nanoclr. Exit code {cliResult.ExitCode}");
                        MessageCentre.OutputVirtualDeviceMessage(cliResult.StandardError);

                        NanoClrInstalled = false;
                    }
                });

                // signal install/update completed
                Messenger.Default.Send(new NotificationMessage(false.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);
            }
        }

        public void UpdateNanoClr()
        {
            var cmd = Cli.Wrap("nanoclr")
                .WithArguments("instance --update")
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 1 minute
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(20));

                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    var cliResult = await cmd.ExecuteBufferedAsync(cts.Token);

                    if (cliResult.ExitCode == 0)
                    {
                        var regexResult = Regex.Match(cliResult.StandardOutput, @"((?>Updated to v)(?'version'\d+\.\d+\.\d+.\d?))");

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
                });
            }
        }
        public string ListVirtualSerialPorts()
        {
            var cmd = Cli.Wrap("nanoclr")
                        .WithArguments("virtualserial --list -v q")
                        .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 1 minute
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(20));

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
        }

        public bool CreateVirtualSerialPort(string portName, out string executionLog)
        {
            var cmd = Cli.Wrap("nanoclr")
                .WithArguments($"virtualserial --create {portName} -v q")
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 1 minute
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(20));

                var cliResult = ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    return await cmd.ExecuteBufferedAsync(cts.Token);
                });

                executionLog = cliResult.StandardOutput;

                return (cliResult.ExitCode == 0);
            }
        }

        public void StopVirtualDevice()
        {
            if (_nanoClrProcess != null)
            {
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Attempting to stop virtual device");

                try
                {
                    _nanoClrProcess.Exited -= VirtualDeviceProcess_Exited;

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
            else
            {
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Attempting to stop virtual device without it being started");
            }
        }

        public async System.Threading.Tasks.Task<bool> StartVirtualDeviceAsync(bool rescanDevices)
        {
            bool tryAgain = true;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);

            // signal start operation
            Messenger.Default.Send(new NotificationMessage(true.ToString()), DeviceExplorerViewModel.MessagingTokens.VirtualDeviceOperationExecuting);

            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Attempting to start virtual device");

            if (_nanoClrProcess != null)
            {
                // this shouldn't happen, still...

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

                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Setting up process to run virtual device");

                // OK to launch process with nanoclr
                _nanoClrProcess = new Process();

                _nanoClrProcess.StartInfo.FileName = "nanoclr";
                _nanoClrProcess.StartInfo.Arguments = $"run --serialport {NanoFrameworkPackage.SettingVirtualDevicePort} --monitorparentpid {Process.GetCurrentProcess().Id}";
                _nanoClrProcess.StartInfo.UseShellExecute = false;
                _nanoClrProcess.StartInfo.CreateNoWindow = true;
                _nanoClrProcess.StartInfo.RedirectStandardOutput = true;
                _nanoClrProcess.StartInfo.RedirectStandardError = true;
                _nanoClrProcess.OutputDataReceived += nanoClrProcess_OutputDataReceived;
                _nanoClrProcess.EnableRaisingEvents = true;

                // enumeration is completed, OK to start virtual device
                MessageCentre.InternalErrorWriteLine($"VirtualDevice: Starting process for virtual device");

            startProces:
                if (_nanoClrProcess.Start())
                {
                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: Process started");

                    _nanoClrProcess.BeginOutputReadLine();

                    if (_nanoClrProcess.WaitForExit(1_000))
                    {
                        // something went wrong starting the process
                        MessageCentre.InternalErrorWriteLine($"VirtualDevice: Process exited prematurely");

                        if (tryAgain)
                        {
                            // lower flag
                            tryAgain = false;

                            Thread.Sleep(5_000);

                            MessageCentre.InternalErrorWriteLine($"VirtualDevice: Retrying to start process");

                            goto startProces;
                        }
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
                    // hasn't started
                    MessageCentre.InternalErrorWriteLine($"VirtualDevice: Failed to start. Exit code was: {_nanoClrProcess.ExitCode}");
                    MessageCentre.OutputVirtualDeviceMessage("");
                    MessageCentre.OutputVirtualDeviceMessage("**************************************************");
                    MessageCentre.OutputVirtualDeviceMessage($"Failed to start virtual device. Exit code was: {_nanoClrProcess.ExitCode}");
                    MessageCentre.OutputVirtualDeviceMessage("**************************************************");
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
            _nanoClrProcess = null;
        }

        private void nanoClrProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                MessageCentre.OutputVirtualDeviceMessage(e.Data);
            }
        }
    }
}

