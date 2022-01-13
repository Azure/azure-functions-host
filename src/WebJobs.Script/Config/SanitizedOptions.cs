// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class SanitizedOptions
    {
        public class EventHubOptions
        {
            public int BatchCheckpointFrequency { get; set; }

            public EventProcessorOptions EventProcessorOptions { get; set; }

            public InitialOffsetOptions InitialOffsetOptions { get; set; }
        }

        public class EventProcessorOptions
        {
            public int MaxBatchSize { get; set; }

            public int PrefetchCount { get; set; }
        }

        public class InitialOffsetOptions
        {
            public string Type { get; set; } = string.Empty;

            public string EnqueuedTimeUTC { get; set; } = string.Empty;
        }

        public class KafkaOptions
        {
            public int? ReconnectBackoffMs { get; set; }

            public int? ReconnectBackoffMaxMs { get; set; }

            public int? StatisticsIntervalMs { get; set; }

            public int? SessionTimeoutMs { get; set; }

            public int? MaxPollIntervalMs { get; set; }

            public int? QueuedMinMessages { get; set; }

            public int? QueuedMaxMessagesKbytes { get; set; }

            public int? MaxPartitionFetchBytes { get; set; }

            public int? FetchMaxBytes { get; set; }

            public int MaxBatchSize { get; set; }

            public int AutoCommitIntervalMs { get; set; }

            public string LibkafkaDebug { get; set; }

            public int? MetadataMaxAgeMs { get; set; }

            public bool? SocketKeepaliveEnable { get; set; }

            public int SubscriberIntervalInSeconds { get; set; }

            public int ExecutorChannelCapacity { get; set; }

            public int ChannelFullRetryIntervalInMs { get; set; }
        }
    }
}
