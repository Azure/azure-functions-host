// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class HostShutdownEvent : ScriptEvent
    {
        public HostShutdownEvent(string source, bool shouldDebounce = true)
           : base(nameof(HostShutdownEvent), source)
        {
            ShouldDebounce = shouldDebounce;
        }

        public bool ShouldDebounce { get; }
    }
}
