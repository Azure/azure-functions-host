// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class StorageBlobContainerExtensions
    {
        public static void CreateIfNotExists(this IStorageBlobContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            container.CreateIfNotExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static bool Exists(this IStorageBlobContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            return container.ExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
