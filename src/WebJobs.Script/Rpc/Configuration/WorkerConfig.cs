﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class WorkerConfig
    {
        public WorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public List<string> Extensions => Description.Extensions;

        public string Language => Description.Language;
    }
}
