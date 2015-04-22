// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class HostSnapshot
    {
        public DateTimeOffset HostVersion { get; set; }

        public IEnumerable<string> FunctionIds { get; set; }
    }
}
