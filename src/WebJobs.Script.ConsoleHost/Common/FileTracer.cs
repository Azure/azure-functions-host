// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.ConsoleHost.Common
{
    public class FileTracer : TraceWriter
    {
        private Collection<TraceEvent> _traces = new Collection<TraceEvent>();
        private readonly StreamWriter _file;

        public FileTracer(TraceLevel level, string filePath) : base(level)
        {
            _file = new StreamWriter(filePath, append: true)
            {
                AutoFlush = true
            };
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (traceEvent.Source == Constants.CliTracingSource)
            {
                _file.WriteLine(traceEvent.Message);
            }
        }
    }
}
