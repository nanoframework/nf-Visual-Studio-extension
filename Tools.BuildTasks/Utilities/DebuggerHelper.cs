//
// Copyright (c) .NET Foundation and Contributors
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

            // this wait should be only available on debug build
            // to prevent unwanted wait on VS in machines where the variable is present
#if DEBUG
            TimeSpan waitForDebugToAttach = TimeSpan.FromSeconds(timeoutSeconds);

            var debugEnabled = Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.User);

            if (!string.IsNullOrEmpty(debugEnabled) && debugEnabled.Equals("1", StringComparison.Ordinal))
            {
                Console.WriteLine($".NET nanoFramework Metadata Processor msbuild instrumentation task debugging is enabled. Waiting {timeoutSeconds} seconds for debugger attachment...");

                var currentProcessId = Process.GetCurrentProcess().Id;
                var currentProcessName = Process.GetProcessById(currentProcessId).ProcessName;
                Console.WriteLine(
                    string.Format("Process Id: {0}, Name: {1}", currentProcessId, currentProcessName)
                    );

                // wait N seconds for debugger to attach
                while (!System.Diagnostics.Debugger.IsAttached && waitForDebugToAttach.TotalSeconds > 0)
                {
                    Thread.Sleep(1000);
                    waitForDebugToAttach -= TimeSpan.FromSeconds(1);
                }

                System.Diagnostics.Debugger.Break();
            }
#endif
        }
    }
}
