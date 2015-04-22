// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
