﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.ManagedDependencies
{
    public class ManagedDependencyOptions
    {
        public bool Enabled { get; set; }
    }
}
