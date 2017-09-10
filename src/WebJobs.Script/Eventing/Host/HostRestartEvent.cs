// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class HostRestartEvent : ScriptEvent
    {
        public HostRestartEvent()
            : base(nameof(HostRestartEvent), EventSources.Worker)
        {
        }
    }
}
