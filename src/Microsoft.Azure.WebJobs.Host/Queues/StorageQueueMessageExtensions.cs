// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    internal static class StorageQueueMessageExtensions
    {
        public static string TryGetAsString(this IStorageQueueMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            string value;

            try
            {
                value = message.AsString;
            }
            catch (DecoderFallbackException)
            {
                // when the message type is binary, AsString will throw if the bytes aren't valid UTF-8.
                value = null;
            }

            return value;
        }
    }
}
