// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Azure.WebJobs.EventHubs;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class RpcClientScriptHostStartupIntegrationTests
{
    [TriggerLifecycleFact("BYOC_TEST_STORAGE_CONNECTION", "BYOC_TEST_EVENTHUB_CONNECTION", "BYOC_TEST_EVENTHUB_NAME", "BYOC_TEST_EVENTHUB_CONSUMER_GROUP")]
    public async Task EventHubListener_WaitsForMatchingWorker_DrainsAtZero_AndRestarts()
    {
        string connection = Environment.GetEnvironmentVariable("BYOC_TEST_EVENTHUB_CONNECTION")!;
        // The hub and consumer group must be dedicated to this test; other consumers would compete for its partitions.
        string eventHubName = Environment.GetEnvironmentVariable("BYOC_TEST_EVENTHUB_NAME")!;
        string consumerGroup = Environment.GetEnvironmentVariable("BYOC_TEST_EVENTHUB_CONSUMER_GROUP")!;
        await using EventHubProducerClient producer = new(connection, eventHubName);
        const string binding =
            """
            {"type":"eventHubTrigger","direction":"in","name":"message","eventHubName":"%LifecycleEventHubName%","consumerGroup":"%LifecycleEventHubConsumerGroup%","connection":"LifecycleEventHubConnection","cardinality":"one","dataType":"string"}
            """;
        await VerifyTriggerListenerLifecycleAsync(
            "EventHub", binding,
            builder => builder.AddEventHubs(options =>
            {
                options.InitialOffsetOptions.Type = OffsetType.FromStart;
                options.LoadBalancingUpdateInterval = TimeSpan.FromSeconds(1);
                options.PartitionOwnershipExpirationInterval = TimeSpan.FromSeconds(5);
            }),
            new Dictionary<string, string>
            {
                ["AzureWebJobsStorage"] = Environment.GetEnvironmentVariable("BYOC_TEST_STORAGE_CONNECTION")!,
                ["LifecycleEventHubConnection"] = connection,
                ["LifecycleEventHubName"] = eventHubName,
                ["LifecycleEventHubConsumerGroup"] = consumerGroup
            },
            async (marker, cancellationToken) =>
            {
                using EventDataBatch batch = await producer.CreateBatchAsync(cancellationToken);
                if (!batch.TryAdd(new EventData(marker)))
                {
                    throw new InvalidOperationException("The lifecycle marker did not fit in an Event Hubs batch.");
                }
                await producer.SendAsync(batch, cancellationToken);
            });
    }
}
