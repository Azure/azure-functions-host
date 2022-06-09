// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    public sealed class WorkerProfileDescriptor
    {
        public string ProfileName { get; set; }

        public RpcWorkerDescription Description { get; set; }

        public IList<WorkerProfileConditionDescriptor> Conditions { get; set; }
    }
}
