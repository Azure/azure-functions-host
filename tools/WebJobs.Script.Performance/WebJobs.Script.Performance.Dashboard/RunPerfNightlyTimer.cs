using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using WebJobs.Script.Tests.Perf.Dashboard.Options;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class RunPerfNightlyTimer
    {
        [FunctionName("RunPerfNightlyTimer")]
        public static async Task Run([TimerTrigger("0 0 14 * * *", RunOnStartup = false)]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"Performance tests were started by timer trigger at: {DateTime.Now}");

            IPerformanceRunOptionsFactory factory = new V2PerformanceRunOptionsFactory(log);
            PerformanceRunOptions options = await factory.CreateAsync();

            await PerformanceManager.Execute(string.Empty, options, log);
        }

        [FunctionName("RunPerfNightlyTimerV3")]
        public static async Task RunV3([TimerTrigger("0 0 16 * * *", RunOnStartup = false)]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"Performance tests were started by timer trigger at: {DateTime.Now}");

            IPerformanceRunOptionsFactory factory = new V3PerformanceRunOptionsFactory(log);
            PerformanceRunOptions options = await factory.CreateAsync();

            await PerformanceManager.Execute(string.Empty, options, log);
        }
    }
}
