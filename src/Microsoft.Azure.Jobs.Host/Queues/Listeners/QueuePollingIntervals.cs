// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal static class QueuePollingIntervals
    {
        public static readonly TimeSpan Minimum = new TimeSpan(0, 0, 2);
        public static readonly TimeSpan Maximum = new TimeSpan(0, 10, 0);
    }
}
