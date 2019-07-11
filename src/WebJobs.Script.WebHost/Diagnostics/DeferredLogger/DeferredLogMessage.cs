// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DeferredLogMessage
    {
        public DateTimeOffset Timestamp { get; internal set; }

        public LogLevel LogLevel { get; internal set; }

        public EventId EventId { get; internal set; }

        public object State { get; internal set; }

        public Exception Exception { get; internal set; }

        public Func<object, Exception, string> Formatter { get; internal set; }

        public string Category { get; internal set; }

        public IEnumerable<object> Scope { get; internal set; }
    }
}