// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Scale;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    internal class TargetScalerVote
    {
        public ScaleVote Vote { get; set; }

        public int TargetWorkerCount { get; set; }
    }
}
