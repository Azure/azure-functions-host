// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal abstract class MutableStorageQueueMessage : IStorageQueueMessage
    {
        public abstract byte[] AsBytes { get; }

        public abstract string AsString { get; }

        public abstract int DequeueCount { get; set; }

        public abstract DateTimeOffset? ExpirationTime { get; set; }

        public abstract string Id { get; set; }

        public abstract DateTimeOffset? InsertionTime { get; set; }

        public abstract DateTimeOffset? NextVisibleTime { get; set; }

        public abstract string PopReceipt { get; set; }

        public abstract CloudQueueMessage SdkObject { get; }
    }
}
