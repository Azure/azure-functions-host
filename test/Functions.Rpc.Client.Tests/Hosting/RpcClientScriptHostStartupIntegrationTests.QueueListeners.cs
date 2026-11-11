// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class RpcClientScriptHostStartupIntegrationTests
{
    [TriggerLifecycleFact("BYOC_TEST_STORAGE_CONNECTION")]
    public async Task QueueListener_DelaysStartsDrainsStopsAndResumesOnNewWorker()
    {
        string connection = Environment.GetEnvironmentVariable("BYOC_TEST_STORAGE_CONNECTION")!;
        string queueName = $"lifecycle-{Guid.NewGuid():N}";
        QueueClient queue = new(connection, queueName, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(4));
        await queue.CreateAsync(cancellationToken: timeout.Token);
        try
        {
            await VerifyTriggerListenerLifecycleAsync(
                "QueueLifecycle",
                $$"""{"name":"message","type":"queueTrigger","direction":"in","queueName":"{{queueName}}","connection":"LifecycleStorage"}""",
                builder => builder.AddAzureStorageQueues(),
                new Dictionary<string, string>
                {
                    ["AzureWebJobsStorage"] = connection,
                    ["AzureWebJobsLifecycleStorage"] = connection,
                },
                async (marker, token) => await queue.SendMessageAsync(marker, cancellationToken: token),
                async (marker, token) =>
                {
                    var pending = await queue.PeekMessagesAsync(32, token);
                    Assert.Contains(pending.Value, message => string.Equals(message.Body.ToString(), marker, StringComparison.Ordinal));
                },
                """{"version":"2.0","extensions":{"queues":{"batchSize":1,"newBatchThreshold":0,"maxPollingInterval":"00:00:01"}}}""");
        }
        finally
        {
            using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromMinutes(1));
            await queue.DeleteIfExistsAsync(cancellationToken: cleanupTimeout.Token);
        }
    }
}
