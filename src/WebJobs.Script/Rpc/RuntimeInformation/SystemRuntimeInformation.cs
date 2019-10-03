// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class SystemRuntimeInformation : ISystemRuntimeInformation
    {
        public Architecture GetOSArchitecture()
        {
            return RuntimeInformation.OSArchitecture;
        }

        public OSPlatform GetOSPlatform()
        {
            OSPlatform os;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                os = OSPlatform.Windows;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                os = OSPlatform.OSX;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                os = OSPlatform.Linux;
            }

            return os;
        }
    }
}
