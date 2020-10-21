﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    public class SharedMemoryMetadata
    {
        public string Name { get; set; }

        public long Count { get; set; }
    }
}
