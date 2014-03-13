using System;
using System.Runtime.InteropServices;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }
}
