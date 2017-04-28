// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public interface IWorkerInfo
    {
        string SiteName { get; }

        string WorkerName { get; }

        string StampName { get; }

        int LoadFactor { get; set; }

        DateTime LastModifiedTimeUtc { get; }

        bool IsStale { get; }

        bool IsHomeStamp { get; }
    }
}
