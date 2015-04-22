// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
