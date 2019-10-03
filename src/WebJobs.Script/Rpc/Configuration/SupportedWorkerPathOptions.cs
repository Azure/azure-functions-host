// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Rpc.Configuration
{
    public static class SupportedWorkerPathOptions
    {
        static SupportedWorkerPathOptions()
        {
            Runtime = new Dictionary<string, WorkerPathOptions>();

            Runtime[LanguageWorkerConstants.PythonLanguageWorkerName] = new WorkerPathOptions()
            {
                Architectures = new List<Architecture>() { Architecture.X64, Architecture.X86 },
                OSPlatforms = new List<OSPlatform>() { OSPlatform.Linux, OSPlatform.Windows, OSPlatform.OSX },
                Versions = new List<string> { "3.6", "3.7" }
            };
        }

        public static IDictionary<string, WorkerPathOptions> Runtime { get; }
    }
}
