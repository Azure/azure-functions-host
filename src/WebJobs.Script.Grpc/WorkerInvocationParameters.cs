// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class WorkerInvocationParameters
    {
        public int TotalInvocations { get; set; }

        public int SuccessfulInvocations { get; set; }

        public double AverageInvocationLatency { get; set; }
    }
}
