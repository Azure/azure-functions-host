// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class RpcChannelEvent : ScriptEvent
    {
        internal RpcChannelEvent(string workerId)
            : base(nameof(RpcChannelEvent), EventSources.Worker)
        {
            WorkerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
        }

        internal string WorkerId { get; private set; }
    }
}