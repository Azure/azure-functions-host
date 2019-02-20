// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerProcessReadyEvent : RpcChannelEvent
    {
        internal WorkerProcessReadyEvent(string workerId, string language)
            : base(workerId)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
        }

        internal string Language { get; private set; }
    }
}
