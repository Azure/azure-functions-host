// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class VersionedFunction
    {
        public string Id { get; set; }

        public string ETag { get; set; }

        public DateTimeOffset HostVersion { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
