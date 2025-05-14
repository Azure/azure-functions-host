using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace App
{
    public sealed class TimerFunctions(ILoggerFactory loggerFactory)
    {
        private readonly ILogger _logger = loggerFactory.CreateLogger<TimerFunctions>();

        [Function("TimerFunction1")]
        public void Run([TimerTrigger("%TIMER_RUN_SCHEDULE_CRON_EXPRESSION%")] TimerInfo timer)
            => _logger.LogInformation($"C# Timer trigger executed at:{DateTime.Now}. IsPastDue:{timer.IsPastDue}");
    }
}
