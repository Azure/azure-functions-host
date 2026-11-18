using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;

namespace NativeDependencyWithTargetFramework
{
    public class NativeDependencyNoRuntimes
    {
        private readonly IConfiguration _config;

        public NativeDependencyNoRuntimes(IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("NativeDependencyNoRuntimes")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            // Issue a query against Cosmos that forces a native assembly to load.
            try
            {
                string cosmosConnection = _config.GetConnectionString("CosmosDB");
                using CosmosClient client = new(cosmosConnection, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
                if (!client.Endpoint.IsLoopback)
                {
                    throw new InvalidOperationException("The native dependency test requires a loopback Cosmos DB emulator endpoint.");
                }

                Database database = await client.CreateDatabaseIfNotExistsAsync("ItemDb");
                Container container = await database.CreateContainerIfNotExistsAsync("ItemCollection", "/id");
                await container.UpsertItemAsync(new { id = "native-query-plan" }, new PartitionKey("native-query-plan"));

                using FeedIterator iterator = container.GetItemQueryStreamIterator("SELECT * FROM c",
                    requestOptions: new QueryRequestOptions { EnableOptimisticDirectExecution = false });
                using ResponseMessage response = await iterator.ReadNextAsync();
                response.EnsureSuccessStatusCode();

                // Gateway transport must still exercise the Windows native query planner.
                using Process process = Process.GetCurrentProcess();
                ProcessModule nativeModule = process.Modules.Cast<ProcessModule>()
                    .SingleOrDefault(m => string.Equals(m.ModuleName, "Microsoft.Azure.Cosmos.ServiceInterop.dll", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("The query did not load the Cosmos native query planner.");
                string expectedDirectory = Path.GetDirectoryName(typeof(NativeDependencyNoRuntimes).Assembly.Location);
                if (!string.Equals(Path.GetDirectoryName(nativeModule.FileName), expectedDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Native module loaded from '{nativeModule.FileName}', expected directory '{expectedDirectory}'.");
                }

                return new OkObjectResult($"Native query succeeded. Loaded: {nativeModule.FileName}");
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.ToString())
                {
                    StatusCode = 500
                };
            }
        }
    }
}
