// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DeferredLogEntry
    {
        public LogLevel LogLevel { get; set; }

        public string Category { get; set; }

        public string Message { get; set; }

        public Exception Exception { get; set; }

        public EventId EventId { get; set; }
    }
}