// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Dashboard.Data
{
    public class VersionedFunction
    {
        public string Id { get; set; }

        public string ETag { get; set; }

        public DateTimeOffset HostVersion { get; set; }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public IDictionary<string, string> Metadata { get; set; }
    }
}
