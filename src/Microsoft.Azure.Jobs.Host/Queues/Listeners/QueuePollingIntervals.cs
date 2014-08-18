// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal static class QueuePollingIntervals
    {
        public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan DefaultMaximum = TimeSpan.FromMinutes(10);
    }
}
