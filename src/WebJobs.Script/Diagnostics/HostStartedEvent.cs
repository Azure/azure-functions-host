// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class HostStarted : MetricEvent
    {
        public HostStarted(ScriptHost host)
        {
            Host = host;
        }

        public ScriptHost Host { get; private set; }
    }
}
