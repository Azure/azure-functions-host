// -----------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class FunctionsWorkerContainerAssignmentContext
    {
        [JsonProperty("assignmentContext")]
        public HostAssignmentContext AssignmentContext { get; private set; }
    }
}
