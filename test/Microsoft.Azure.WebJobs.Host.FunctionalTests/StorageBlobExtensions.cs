// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class StorageBlobExtensions
    {
        public static string DownloadText(this IStorageBlob blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            using (Stream stream = blob.OpenReadAsync(CancellationToken.None).GetAwaiter().GetResult())
            using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
            {
                return reader.ReadToEnd();
            }
        }

        public static bool Exists(this IStorageBlob blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            return blob.ExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
