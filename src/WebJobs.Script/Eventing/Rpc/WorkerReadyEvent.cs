// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerReadyEvent : ScriptEvent
    {
        public WorkerReadyEvent()
            : base(nameof(WorkerReadyEvent), EventSources.Worker)
        {
        }

        public string Id { get; set; }

        public string Version { get; set; }

        public IDictionary<string, string> Capabilities { get; set; }

        public WorkerConfig Config { get; set; }
    }
}
