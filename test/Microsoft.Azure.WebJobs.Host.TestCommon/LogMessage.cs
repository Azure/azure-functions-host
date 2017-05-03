// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class LogMessage
    {
        public LogLevel Level { get; set; }
        public EventId EventId { get; set; }
        public IEnumerable<KeyValuePair<string, object>> State { get; set; }
        public Exception Exception { get; set; }
        public string FormattedMessage { get; set; }

        public string Category { get; set; }
    }
}
