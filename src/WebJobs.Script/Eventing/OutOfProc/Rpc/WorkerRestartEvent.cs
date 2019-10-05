// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerRestartEvent : RpcChannelEvent
    {
        internal WorkerRestartEvent(string language, string workerId)
            : base(workerId)
        {
            Language = language;
        }

        internal string Language { get; }
    }
}
