﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    // Arguments to start a worker process
    internal class HttpWorkerContext : WorkerContext
    {
        public int Port { get; set; }

        public override string GetFormattedArguments()
        {
            // Ensure parsing cmd args is not required by httpworker
            return string.Empty;
        }
    }
}
