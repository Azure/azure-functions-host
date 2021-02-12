using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
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

                var client = new DocumentClient(new Uri(dbUri.ToString()), dbKey.ToString());
                Uri collUri = UriFactory.CreateDocumentCollectionUri("ItemDb", "ItemCollection");

                var options = new FeedOptions
                {
                    EnableCrossPartitionQuery = true
                };

                IDocumentQuery<Document> documentQuery = client.CreateDocumentQuery<Document>(collUri, "SELECT * FROM c WHERE STARTSWITH(c.id, @PartitionLeasePrefix)", options).AsDocumentQuery<Document>();

                await documentQuery.ExecuteNextAsync();
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
