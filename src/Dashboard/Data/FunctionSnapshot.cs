// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class FunctionSnapshot
    {
        public DateTimeOffset HostVersion { get; set; }

        public string Id { get; set; }

        public string QueueName { get; set; }

        public string HeartbeatSharedContainerName { get; set; }

        public string HeartbeatSharedDirectoryName { get; set; }

        public int? HeartbeatExpirationInSeconds { get; set; }

        public string HostFunctionId { get; set; }

        public string FullName { get; set; }

        public string ShortName { get; set; }

        public IDictionary<string, ParameterSnapshot> Parameters { get; set; }
    }
}
