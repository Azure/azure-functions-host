// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SystemEnvironment : IEnvironment
    {
        private static readonly Lazy<SystemEnvironment> _instance = new Lazy<SystemEnvironment>(CreateInstance);

        private SystemEnvironment()
        {
        }

        public static SystemEnvironment Instance => _instance.Value;

        public bool Is64BitProcess => Environment.Is64BitProcess;

        public OSPlatform Platform { get; } = GetCurrentPlatform();

        private static SystemEnvironment CreateInstance()
        {
            return new SystemEnvironment();
        }

        public string GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        internal static OSPlatform GetCurrentPlatform()
        {
            if (OperatingSystem.IsWindows())
            {
                return OSPlatform.Windows;
            }

            if (OperatingSystem.IsLinux())
            {
                return OSPlatform.Linux;
            }

            if (OperatingSystem.IsMacOS())
            {
                return OSPlatform.OSX;
            }

            if (OperatingSystem.IsFreeBSD())
            {
                return OSPlatform.FreeBSD;
            }

            return OSPlatform.Create("Unknown");
        }
    }
}
