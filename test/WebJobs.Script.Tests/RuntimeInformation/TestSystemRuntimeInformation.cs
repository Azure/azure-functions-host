// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestSystemRuntimeInformation : ISystemRuntimeInformation
    {
        public Architecture GetOSArchitecture()
        {
            return Architecture.X64;
        }

        public OSPlatform GetOSPlatform()
        {
            return OSPlatform.Linux;
        }
    }
}
