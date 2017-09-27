// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public class WorkerCreateContext
    {
        public Uri ServerUri { get; set; }

        public ArgumentsDescription Arguments { get; set; }

        public string WorkerId { get; set; }

        public string RequestId { get; set; }

        public string WorkingDirectory { get; set; }
    }
}
