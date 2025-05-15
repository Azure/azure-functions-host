// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class HostRestartEvent : ScriptEvent
    {
        public HostRestartEvent(string reason)
            : base(nameof(HostRestartEvent), EventSources.Worker)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
