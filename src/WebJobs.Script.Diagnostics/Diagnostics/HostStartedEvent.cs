// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WebJobs.Script;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class HostStarted : MetricEvent
    {
        public HostStarted(IScriptHost host)
        {
            Host = host;
        }

        public IScriptHost Host { get; private set; }
    }
}
