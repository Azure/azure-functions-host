// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class WorkerInvocationParameters
    {
        public int TotalInvocations { get; set; }

        public int SuccessfulInvocations { get; set; }

        public double AverageInvocationLatency { get; set; }
    }
}
