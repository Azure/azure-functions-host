using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class Dashboard
    {
        [FunctionName("Dashboard")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ExecutionContext context,
            ILogger log)
        {
            try
            {
                string year = req.Query["year"];
                string month = req.Query["month"];
                string day = req.Query["day"];
                bool.TryParse(req.Query["onlyWarnings"], out bool onlyWarnings);

                string tableContent = await ReportProcessor.GetHtmlReport(year, month, day, onlyWarnings);

                string content = File.ReadAllText($"{context.FunctionAppDirectory}\\template.html");
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
