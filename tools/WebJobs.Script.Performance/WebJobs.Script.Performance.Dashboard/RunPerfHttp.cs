
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using WebJobs.Script.Tests.Perf.Dashboard.Options;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class RunPerfHttp
    {
        [FunctionName("RunPerfHttp")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, ILogger log)
        {
            try
            {
                log.LogInformation($"Performance tests were started by http trigger at: {DateTime.Now}");

                string testId = string.Empty;
                req.GetQueryParameterDictionary().TryGetValue("testId", out testId);

                if (!req.GetQueryParameterDictionary().TryGetValue("version", out string version))
                {
                    return new BadRequestObjectResult("Specify 'version' of 'v2' or 'v3' on the query string.");
                }

                PerformanceRunOptions options = null;
                switch (version.ToLowerInvariant())
                {
                    case "v2":
                        IPerformanceRunOptionsFactory v2factory = new V2PerformanceRunOptionsFactory(log);
                        options = await v2factory.CreateAsync();
                        break;
                    case "v3":
                        IPerformanceRunOptionsFactory v3factory = new V3PerformanceRunOptionsFactory(log);
                        options = await v3factory.CreateAsync();
                        break;
                    default:
                        return new BadRequestObjectResult("Specify 'version' of 'v2' or 'v3' on the query string.");
                }

                await PerformanceManager.Execute(testId, options, log);

                return new ContentResult()
                {
                    Content = string.IsNullOrEmpty(testId) ? "All tests started" : $"Tests started: {testId}",
                    ContentType = "text/html"
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());
                return new ContentResult()
                {
                    Content = "Exception:" + ex,
                    ContentType = "text/html"
                };
            }
        }
    }
}
