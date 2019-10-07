// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class HttpWorkerRestartEvent : ScriptEvent
    {
        public HttpWorkerRestartEvent(string workerId)
            : base(nameof(HttpWorkerRestartEvent), EventSources.Rpc)
        {
            WorkerId = workerId;
        }

        public string WorkerId { get; }
    }
}
