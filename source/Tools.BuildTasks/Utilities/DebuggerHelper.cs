//
// Copyright (c) The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Threading;

namespace nanoFramework.Tools.Utilities
{
    public static class DebuggerHelper
    {
        public static void WaitForDebuggerIfEnabled(string varName, int timeoutSeconds = 30)
        {
            TimeSpan waitForDebugToAttach = TimeSpan.FromSeconds(timeoutSeconds);

            var debugEnabled = Environment.GetEnvironmentVariable(varName);
            if (!string.IsNullOrEmpty(debugEnabled) && debugEnabled.Equals("1", StringComparison.Ordinal))
            {
                Console.WriteLine($"nanoFramework Metadata Processor msbuild instrumentation task debugging is enabled. Waiting {timeoutSeconds} seconds for debugger attachment...");

                var currentProcessId = Process.GetCurrentProcess().Id;
                var currentProcessName = Process.GetProcessById(currentProcessId).ProcessName;
                Console.WriteLine(
                    string.Format("Process Id: {0}, Name: {1}", currentProcessId, currentProcessName)
                    );

                // wait N seconds for debugger to attach
                while (!Debugger.IsAttached && waitForDebugToAttach.TotalSeconds > 0)
                {
                    Thread.Sleep(1000);
                    waitForDebugToAttach -= TimeSpan.FromSeconds(1);
                }

                Debugger.Break();
            }
        }
    }
}
