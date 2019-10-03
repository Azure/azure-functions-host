// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script.Rpc.Configuration
{
    public class WorkerPathOptions
    {
        public IList<Architecture> Architectures { get; set; }

        public IList<OSPlatform> OSPlatforms { get; set; }

        public IList<string> Versions { get; set; }
    }
}
