// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class TestTraceWriter : TraceWriter
    {
        public Collection<string> Traces = new Collection<string>();

        public TestTraceWriter(TraceLevel level) : base(level)
        {
        }

        public override void Trace(TraceLevel level, string source, string message, Exception ex)
        {
            string trace = string.Format("{0} {1} {2} {3}", level, source, message, ex);
            Traces.Add(trace);
        }
    }
}
