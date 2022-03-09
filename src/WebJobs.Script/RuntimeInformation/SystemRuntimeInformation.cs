// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SystemRuntimeInformation : ISystemRuntimeInformation
    {
        private static OSPlatform? _platform;
        private static Lazy<SystemRuntimeInformation> _runtimeInformationInstance = new Lazy<SystemRuntimeInformation>(new SystemRuntimeInformation());

        public static ISystemRuntimeInformation Instance => _runtimeInformationInstance.Value;

        public Architecture GetOSArchitecture()
        {
            return RuntimeInformation.OSArchitecture;
        }

        public OSPlatform GetOSPlatform()
        {
            if (_platform == null)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _platform = OSPlatform.Windows;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    _platform = OSPlatform.OSX;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    _platform = OSPlatform.Linux;
                }
            }

            return _platform.Value;
        }
    }
}
