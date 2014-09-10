// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class HostQueueNames
    {
        private const string Prefix = "azure-webjobs-";

        private const string HostBlobTriggerQueuePrefix = Prefix + "blobtrigger-";
        private const string HostQueuePrefix = Prefix + "host-";

        // The standard prefix is too long here; this queue is bound by customers.
        public static string BlobTriggerPoisonQueue = "webjobs-blobtrigger-poison";

        /// <summary>Gets the shared host blob trigger queue name.</summary>
        /// <param name="hostId">The host ID.</param>
        /// <returns>The shared host blob trigger queue name.</returns>
        public static string GetHostBlobTriggerQueueName(string hostId)
        {
            return HostBlobTriggerQueuePrefix + hostId;
        }

        /// <summary>Gets the host instance queue name.</summary>
        /// <param name="hostId">The host ID.</param>
        /// <returns>The host instance queue name.</returns>
        public static string GetHostQueueName(string hostId)
        {
            return HostQueuePrefix + hostId;
        }
    }
}
