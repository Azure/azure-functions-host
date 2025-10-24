// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public abstract class CosmosDBEndtoEndTestFixture : EndToEndTestFixture
    {
        [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification = "Well known account key for emulator. Used for testing.")]
        public const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

        private static string CosmosDBEndpoint => "https://localhost:65000/";

        protected CosmosDBEndtoEndTestFixture(string rootPath, string testId, string language) :
            base(rootPath, testId, language)
        {
        }

        public CosmosClient CosmosClient { get; private set; }

        protected override ExtensionPackageReference[] GetExtensionsToInstall()
        {
            return new ExtensionPackageReference[]
            {
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                        Version = "4.11.0"
                    }
            };
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
            {
                o.Functions = new[]
                {
                    "CosmosDBTrigger",
                    "CosmosDBIn",
                    "CosmosDBOut"
                };
            });
        }

        public void InitializeCosmosClient()
        {
            if (CosmosClient is null)
            {
                CosmosClient = new(
                    accountEndpoint: CosmosDBEndpoint,
                    authKeyOrResourceToken: EmulatorKey
                );
            }
        }

        public override async Task InitializeAsync()
        {
            if (IsEmulatorRunning())
            {
                await base.InitializeAsync();
            }
            else
            {
                throw new Exception("CosmosDB Emulator is not running. Skipping tests."); // TODO: review exception here
            }
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            CosmosClient?.Dispose();
        }

        public async Task<bool> CreateContainers()
        {
            bool collectionsCreated = false;

            DatabaseResponse databaseResponse = await CosmosClient.CreateDatabaseIfNotExistsAsync("ItemDb");
            Database database = databaseResponse.Database;

            ContainerProperties itemCollectionProperties = new ContainerProperties("ItemCollection", "/partitionKey");
            ContainerResponse itemCollectionResponse = await database.CreateContainerIfNotExistsAsync(itemCollectionProperties, throughput: 400);

            ContainerProperties leasesCollectionProperties = new ContainerProperties("leases", "/partitionKey");
            ContainerResponse leasesCollectionResponse = await database.CreateContainerIfNotExistsAsync(leasesCollectionProperties, throughput: 400);

            if (itemCollectionResponse.StatusCode == System.Net.HttpStatusCode.Created
                && leasesCollectionResponse.StatusCode == System.Net.HttpStatusCode.Created)
            {
                collectionsCreated = true;
            }

            return collectionsCreated;
        }

        public async Task DeleteContainers()
        {
            Database database = CosmosClient.GetDatabase("ItemDb");

            // Delete the "ItemCollection" container
            Container itemCollectionContainer = database.GetContainer("ItemCollection");
            await itemCollectionContainer.DeleteContainerAsync();

            // Delete the "leases" container
            Container leasesContainer = database.GetContainer("leases");
            await leasesContainer.DeleteContainerAsync();
        }

        public bool IsEmulatorRunning()
        {
            try
            {
                // Parse the CosmosDBConnection variable
                var connectionUri = new Uri(CosmosDBEndpoint);
                string host = connectionUri.Host;
                int port = connectionUri.Port;

                // Attempt to connect to the specified host and port
                using TcpClient client = new();
                var connectTask = client.ConnectAsync(host, port);
                return connectTask.Wait(TimeSpan.FromSeconds(2)); // Timeout after 2 seconds
            }
            catch
            {
                // If any exception occurs, assume the connection is unavailable
                return false;
            }
        }
    }
}
