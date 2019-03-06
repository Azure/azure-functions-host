using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class SendReportHttp
    {
        [FunctionName("SendReportHttp")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, 
            ILogger log, ExecutionContext context, [SendGrid()] IAsyncCollector<SendGridMessage> messageCollector)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string warnings = await ReportProcessor.GetLastDaysHtmlReport(7, true);
            string all = await ReportProcessor.GetLastDaysHtmlReport(7, false);

            string content = await SendEmail(warnings, all, context, messageCollector);

            return new ContentResult()
            {
                Content = content,
                ContentType = "text/html",
            };
        }

        public static async Task<string> SendEmail(string warnings, string all, ExecutionContext context, IAsyncCollector<SendGridMessage> messageCollector)
        {
            string content = File.ReadAllText($"{context.FunctionAppDirectory}\\TemplateEmail.html");
            content = content.Replace("[replace_warning]", warnings);
            content = content.Replace("[replace_all]", all);

            var message = new SendGridMessage();
            string[] toArr = Environment.GetEnvironmentVariable("SendEmailTo").Split(';');
            foreach (string email in toArr)
            {
                if (!string.IsNullOrEmpty(email))
                {
                    message.AddTo(email);
                }
            }
            message.AddContent("text/html", content);
            message.SetFrom(Environment.GetEnvironmentVariable("SendEmailFrom"));
            message.SetSubject("Daily Functions rutime performance report");

            await messageCollector.AddAsync(message);

            return content;
        }
    }
}
