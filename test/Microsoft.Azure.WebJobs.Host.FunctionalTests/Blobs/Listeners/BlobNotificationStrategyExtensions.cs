// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.Blobs.Listeners
{
    internal static class BlobNotificationStrategyExtensions
    {
        public static void Execute(this IBlobNotificationStrategy strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException("strategy");
            }

            strategy.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static void Register(this IBlobNotificationStrategy strategy, IStorageBlobContainer container,
            ITriggerExecutor<IStorageBlob> triggerExecutor)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException("strategy");
            }

            strategy.RegisterAsync(container, triggerExecutor, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
