// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class ScriptHostStartedEvent : MetricEvent
    {
        public ScriptHostStartedEvent(ScriptHost scriptHost)
        {
            ScriptHost = scriptHost;
        }
        public ScriptHost ScriptHost { get; private set; }
    }
}
