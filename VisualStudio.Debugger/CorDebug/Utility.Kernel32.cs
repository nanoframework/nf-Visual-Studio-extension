using CorDebugInterop;
using System;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Debugger
{
    public partial class Utility
    {
        public class Kernel32
        {
            public const int DUPLICATE_SAME_ACCESS = 0x00000002;
            public const uint CREATE_SUSPENDED = 0x00000004;

            public delegate void CreateThreadCallback(IntPtr lpParameter);

            [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool CreateProcessW(string appName, string cmdLine, IntPtr lpProcessAttrs, IntPtr lpThreadAttrs, int bInheritHandles, uint dwCreatingFlags, IntPtr lpEnvironment, string curDir, ref _STARTUPINFO info, ref _PROCESS_INFORMATION pinfo);
            [DllImport("kernel32.dll")]
            public static extern IntPtr GetCurrentThread();
            [DllImport("kernel32.dll")]
            public static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions);
            [DllImport("kernel32.dll")]
            public static extern bool CloseHandle(IntPtr hObject);
            [DllImport("kernel32.dll")]
            public static extern IntPtr GetCurrentProcess();
            [DllImport("kernel32.dll")]
            public static extern int SuspendThread(IntPtr hThread);
            [DllImport("kernel32.dll")]
            public static extern int ResumeThread(IntPtr hThread);
            [DllImport("kernel32.dll")]
            public static extern int GetCurrentThreadId();
            [DllImport("kernel32.dll")]
            public static extern int GetLastError();
            [DllImport("kernel32.dll")]
            public static extern IntPtr CreateThread(IntPtr lpsa, uint cbStack, CreateThreadCallback lpStartAddr, IntPtr lpvThreadParam, uint fdwCreate, out uint threadId);
        }
    }
}
