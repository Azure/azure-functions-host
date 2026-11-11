// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class RpcClientScriptHostStartupIntegrationTests
{
    [TriggerLifecycleFact("BYOC_TEST_STORAGE_CONNECTION", "BYOC_TEST_COSMOS_CONNECTION")]
    public async Task CosmosListener_WaitsForWorker_DrainsAtZero_AndResumesFromCheckpoint()
    {
        string connection = Environment.GetEnvironmentVariable("BYOC_TEST_COSMOS_CONNECTION")!;
        DbConnectionStringBuilder connectionSettings = new() { ConnectionString = connection };
        if (connectionSettings.TryGetValue("DisableServerCertificateValidation", out object? disableValidation) &&
            bool.Parse((string)disableValidation))
        {
            Uri endpoint = new((string)connectionSettings["AccountEndpoint"]);
            Assert.True(endpoint.IsLoopback, "Certificate validation may only be disabled for the loopback Cosmos emulator.");
        }

        using CosmosClient client = new(connection, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        using CancellationTokenSource setupTimeout = new(TimeSpan.FromMinutes(4));
        string databaseName = $"byoc-lifecycle-{Guid.NewGuid():N}";
        Database database = await client.CreateDatabaseAsync(databaseName, cancellationToken: setupTimeout.Token);
        try
        {
            Container items = await database.CreateContainerAsync("items", "/id", cancellationToken: setupTimeout.Token);
            Container leases = await database.CreateContainerAsync("leases", "/id", cancellationToken: setupTimeout.Token);
            bool initialLeaseCheck = true;
            Dictionary<string, string>? stoppedCheckpoints = null;
            string bindingJson = JsonSerializer.Serialize(new
            {
                name = "documents",
                type = "cosmosDBTrigger",
                direction = "in",
                databaseName,
                containerName = "items",
                connection = "LifecycleCosmos",
                leaseContainerName = "leases",
                startFromBeginning = true,
                feedPollDelay = 100,
                leaseAcquireInterval = 1000,
                maxItemsPerInvocation = 1,
            });

            await VerifyTriggerListenerLifecycleAsync(
                "CosmosLifecycle",
                bindingJson,
                builder => builder.AddCosmosDB(options => options.ConnectionMode = ConnectionMode.Gateway),
                new Dictionary<string, string>
                {
                    ["AzureWebJobsStorage"] = Environment.GetEnvironmentVariable("BYOC_TEST_STORAGE_CONNECTION")!,
                    ["LifecycleCosmos"] = connection,
                },
                async (marker, cancellationToken) =>
                {
                    await items.CreateItemAsync(new { id = marker, marker }, new PartitionKey(marker), cancellationToken: cancellationToken);
                },
                async (_, cancellationToken) =>
                {
                    List<JsonElement> documents = await ReadCosmosLeasesAsync(leases, cancellationToken);
                    if (initialLeaseCheck)
                    {
                        initialLeaseCheck = false;
                        Assert.Empty(documents);
                        return;
                    }

                    // StopAsync must release ownership without deleting the checkpoint needed by the next listener.
                    Assert.NotEmpty(documents);
                    Assert.DoesNotContain(documents, document =>
                        document.TryGetProperty("Owner", out JsonElement owner) && !string.IsNullOrEmpty(owner.GetString()));
                    Dictionary<string, string> currentCheckpoints = GetCosmosCheckpoints(documents);
                    Assert.NotEmpty(currentCheckpoints);
                    if (stoppedCheckpoints is not null)
                    {
                        AssertCosmosCheckpointsAdvanced(stoppedCheckpoints, currentCheckpoints);
                    }

                    stoppedCheckpoints = currentCheckpoints;
                });

            Dictionary<string, string> checkpoints = Assert.IsType<Dictionary<string, string>>(stoppedCheckpoints);
            using CancellationTokenSource checkpointTimeout = new(TimeSpan.FromMinutes(1));
            List<JsonElement> resumedLeases = await ReadCosmosLeasesAsync(leases, checkpointTimeout.Token);
            Dictionary<string, string> resumedCheckpoints = GetCosmosCheckpoints(resumedLeases);
            AssertCosmosCheckpointsAdvanced(checkpoints, resumedCheckpoints);
        }
        finally
        {
            // Only this test's uniquely named database and its two containers are owned by this test.
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    private static async Task<List<JsonElement>> ReadCosmosLeasesAsync(Container leases, CancellationToken cancellationToken)
    {
        List<JsonElement> documents = [];
        using FeedIterator iterator = leases.GetItemQueryStreamIterator("SELECT * FROM c");
        while (iterator.HasMoreResults)
        {
            using ResponseMessage response = await iterator.ReadNextAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument page = await JsonDocument.ParseAsync(response.Content, cancellationToken: cancellationToken);
            documents.AddRange(page.RootElement.GetProperty("Documents").EnumerateArray().Select(document => document.Clone()));
        }

        return documents;
    }

    private static Dictionary<string, string> GetCosmosCheckpoints(IEnumerable<JsonElement> leases) =>
        leases.Where(document => document.TryGetProperty("ContinuationToken", out JsonElement token) &&
            token.ValueKind is JsonValueKind.String && !string.IsNullOrEmpty(token.GetString()))
            .ToDictionary(document => document.GetProperty("id").GetString()!,
                document => document.GetProperty("ContinuationToken").GetString()!, StringComparer.Ordinal);

    private static void AssertCosmosCheckpointsAdvanced(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current)
    {
        Assert.Equal(previous.Keys.Order(), current.Keys.Order());
        Assert.Contains(previous, checkpoint =>
            !string.Equals(checkpoint.Value, current[checkpoint.Key], StringComparison.Ordinal));
    }
}
