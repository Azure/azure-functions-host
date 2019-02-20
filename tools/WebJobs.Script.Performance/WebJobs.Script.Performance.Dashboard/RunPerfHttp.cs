
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class RunPerfHttp
    {
        [FunctionName("RunPerfHttp")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, ILogger log)
        {
            log.LogInformation($"Performance tests were started by http trigger at: {DateTime.Now}");

            string testIds = string.Empty;
            req.GetQueryParameterDictionary().TryGetValue("testIds", out testIds);

            await PerformanceManager.Execute(testIds, log);

            return new ContentResult()
            {
                Content = "Done",
                ContentType = "text/html",
            };
        }
    }
}
