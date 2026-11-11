// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class RpcClientScriptHostStartupIntegrationTests
{
    // Also run with -p:BlobExtensionVersion=5.3.7, shipped in extension bundle 4.41.1.
    [TriggerLifecycleFact("BYOC_TEST_STORAGE_CONNECTION")]
    public Task EventGridBlobListener_WaitsForWorker_DrainsAtZero_AndResumes() => VerifyEventGridBlobListenerLifecycleAsync(false);

    [TriggerLifecycleFact("BYOC_TEST_STORAGE_CONNECTION")]
    public Task EventGridBlobListeners_ShareQueueGateAcrossPauseAndResume() => VerifyEventGridBlobListenerLifecycleAsync(true);

    private static async Task VerifyEventGridBlobListenerLifecycleAsync(bool multipleFunctions)
    {
        string connection = Environment.GetEnvironmentVariable("BYOC_TEST_STORAGE_CONNECTION")!;
        string containerName = $"lifecycle-{Guid.NewGuid():N}";
        string hostId = $"blob-{Guid.NewGuid():N}"[..32];
        string[] functions = multipleFunctions ? ["BlobLifecycle", "SecondBlobLifecycle"] : ["BlobLifecycle"];
        string binding = $$"""{"name":"blob","type":"blobTrigger","direction":"in","path":"{{containerName}}/{name}","source":"EventGrid","connection":"LifecycleStorage","dataType":"string"}""";
        BlobContainerClient container = new(connection, containerName);
        QueueClient queue = new(connection, $"azure-webjobs-blobtrigger-{hostId}",
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        using CancellationTokenSource setupTimeout = new(TimeSpan.FromMinutes(2));
        try
        {
            await container.CreateAsync(cancellationToken: setupTimeout.Token);
            await queue.CreateAsync(cancellationToken: setupTimeout.Token);

            await VerifyTriggerListenerLifecycleAsync(
                "BlobLifecycle",
                binding,
                builder => builder.AddAzureStorageBlobs(),
                new Dictionary<string, string>
                {
                    ["AzureWebJobsStorage"] = connection,
                    ["AzureWebJobsLifecycleStorage"] = connection,
                },
                async (marker, token) =>
                {
                    var blob = container.GetBlobClient(marker);
                    var upload = await blob.UploadAsync(BinaryData.FromString(marker), cancellationToken: token);

                    // This is the internal message normally enqueued by the Event Grid webhook.
                    foreach (string function in marker.EndsWith("-held", StringComparison.Ordinal) ? functions.Take(1) : functions)
                    {
                        string message = JsonSerializer.Serialize(new
                        {
                            Type = "BlobTrigger",
                            FunctionId = $"Host.Functions.{function}",
                            BlobType = "BlockBlob",
                            ContainerName = containerName,
                            BlobName = marker,
                            ETag = upload.Value.ETag.ToString(),
                        });
                        await queue.SendMessageAsync(message, cancellationToken: token);
                    }
                },
                async (marker, token) =>
                {
                    var pending = await queue.PeekMessagesAsync(32, token);
                    foreach (string function in functions)
                    {
                        var message = Assert.Single(pending.Value, message =>
                            message.Body.ToString().Contains(marker, StringComparison.Ordinal) &&
                            message.Body.ToString().Contains($"Host.Functions.{function}", StringComparison.Ordinal));
                        Assert.Equal(0, message.DequeueCount);
                    }
                },
                functionGroup: FunctionGroups.Blob,
                hostId: hostId,
                additionalFunctions: multipleFunctions ? new Dictionary<string, string> { ["SecondBlobLifecycle"] = binding } : null,
                startOnly: true);
        }
        finally
        {
            using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromMinutes(1));
            try
            {
                await queue.DeleteIfExistsAsync(cancellationToken: cleanupTimeout.Token);
            }
            finally
            {
                await container.DeleteIfExistsAsync(cancellationToken: cleanupTimeout.Token);
            }
        }
    }
}
