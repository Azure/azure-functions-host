// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class HostSnapshot
    {
        public DateTimeOffset HostVersion { get; set; }

        public IEnumerable<FunctionSnapshot> Functions { get; set; }
    }
}
