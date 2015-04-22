// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Queues;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal static class HostIdValidator
    {
        // The Host ID will be used as a queue name (after adding some prefixes).
        // The longer of the two is HostQueueNames.BlobTriggerPoisonQueue, and queue names must be less than 63
        // characters. Keep enough space for the longest current queue prefix plus some buffer.
        private const int MaximumHostIdLength = 32;

        public static string ValidationMessage
        {
            get
            {
                return "A host ID must be between 1 and 32 characters, contain only lowercase letters, numbers, and " +
                    "dashes, not start or end with a dash, and not contain consecutive dashes.";
            }
        }

        public static bool IsValid(string hostId)
        {
            if (String.IsNullOrEmpty(hostId))
            {
                return false;
            }

            if (hostId.Length > MaximumHostIdLength)
            {
                return false;
            }

            string longestPrefixedQueueName = HostQueueNames.GetHostBlobTriggerQueueName(hostId);

            if (!QueueClient.IsValidQueueName(longestPrefixedQueueName))
            {
                return false;
            }

            return true;
        }
    }
}
