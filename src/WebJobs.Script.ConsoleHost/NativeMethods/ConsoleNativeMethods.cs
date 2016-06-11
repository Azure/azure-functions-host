// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WebJobs.Script.ConsoleHost.NativeMethods
{
    public static class ConsoleNativeMethods
    {
        // http://stackoverflow.com/a/36029698/3234163
        public enum StdHandle
        {
            Input = -10,
            Output = -11,
            Error = -12,
        }

        public enum ConsoleMode
        {
            ENABLE_ECHO_INPUT = 4
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(StdHandle nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

        public static string ReadPassword()
        {
            IntPtr stdInputHandle = GetStdHandle(StdHandle.Input);
            if (stdInputHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("No console input");
            }

            int previousConsoleMode;
            if (!GetConsoleMode(stdInputHandle, out previousConsoleMode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not get console mode.");
            }

            // disable console input echo
            if (!SetConsoleMode(stdInputHandle, previousConsoleMode & ~(int)ConsoleMode.ENABLE_ECHO_INPUT))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not disable console input echo.");
            }

            // just read the password using standard Console.ReadLine()
            string password = Console.ReadLine();

            // reset console mode to previous
            if (!SetConsoleMode(stdInputHandle, previousConsoleMode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not reset console mode.");
            }

            return password;

        }
    }
}
