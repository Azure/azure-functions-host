// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    public class FunctionSnapshot
    {
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
