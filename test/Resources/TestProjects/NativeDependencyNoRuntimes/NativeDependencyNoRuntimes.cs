using System;
using System.Data.Common;
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
                var builder = new DbConnectionStringBuilder()
                {
                    ConnectionString = cosmosConnection
                };

                builder.TryGetValue("AccountEndpoint", out object dbUri);
                builder.TryGetValue("AccountKey", out object dbKey);

                var client = new CosmosClient(dbUri.ToString(), dbKey.ToString());

                Container container = client.GetDatabase("ItemDb").GetContainer("ItemCollection");
                FeedIterator iterator = container.GetItemQueryStreamIterator("SELECT * FROM c");
                await iterator.ReadNextAsync();
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.ToString())
                {
                    StatusCode = 500
                };
            }

            return new OkResult();
        }
    }
}
