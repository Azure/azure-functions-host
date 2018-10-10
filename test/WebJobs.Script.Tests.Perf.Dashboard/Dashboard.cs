
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage;
using System;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Linq;
using System.Threading.Tasks;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class Dashboard
    {
        [FunctionName("Dashboard")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            try
            {
                string month = req.Query["month"];
                string year = req.Query["year"];

                string blobConectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage", EnvironmentVariableTarget.Process);
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(blobConectionString);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference("dashboard");

                BlobResultSegment result = await container.ListBlobsSegmentedAsync($"{year ?? "2018"}-{month ?? "10"}", null);
                string tableContent = "";
                foreach (var item in result.Results.OrderByDescending(x => x.StorageUri.ToString()))
                {
                    string blobUri = item.Uri.ToString().TrimEnd('/');
                    string time = blobUri.Split('/').Last().Replace("-", "/").Replace("_", ":");
                    CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobUri.Split('/').Last() + "/summary.txt");
                    string summary = await blockBlob.DownloadTextAsync();
                    string[] summaryNumbers = summary.Split(',');
                    int totalRequests = int.Parse(summaryNumbers[2]);
                    double avgResponseTime = Math.Round(double.Parse(summaryNumbers[5]), 2);
                    int rps = totalRequests / 120;
                    tableContent += $"<tr><td>{time}</td><td><a href='{blobUri}/index.html'>{summaryNumbers[0]}</a></td><td>{summaryNumbers[1]}</td><td>{rps}</td><td>{totalRequests}</td><td>{avgResponseTime}</td></tr>";
                }

                string content = File.ReadAllText(@"d:\home\site\wwwroot\template.html");
                content = content.Replace("[replace]", tableContent);

                return new ContentResult()
                {
                    Content = content,
                    ContentType = "text/html",
                };
            }
            catch (Exception ex)
            {
                return new ContentResult()
                {
                    Content = ex.ToString(),
                    ContentType = "text/html",
                };
            }
        }
    }
}
