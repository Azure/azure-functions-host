// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class WorkerInfo
    {
        public WorkerInfo(string id, string version, IDictionary<string, string> capabilites)
        {
            Id = id;
            Version = version;
            Capabilities = capabilites;
        }

        public string Id { get; }

        public string Version { get; }

        public IDictionary<string, string> Capabilities { get; }
    }
}
