using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class SendReportTimer
    {
        [FunctionName("SendReportTimer")]
        public static async Task Run(
            [TimerTrigger("0 0 17 * * *", RunOnStartup=false)]TimerInfo myTimer, ILogger log, ExecutionContext context,
            [SendGrid()] IAsyncCollector<SendGridMessage> messageCollector)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            string warnings = await ReportProcessor.GetLastDaysHtmlReport(7, true);
            string all = await ReportProcessor.GetLastDaysHtmlReport(7, false);

            await SendReportHttp.SendEmail(warnings, all, context, messageCollector);
        }
    }
}
