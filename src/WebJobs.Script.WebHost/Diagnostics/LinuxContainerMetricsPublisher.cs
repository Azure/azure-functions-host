using Microsoft.Azure.WebJobs.Script.Metrics;
using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxContainerMetricsPublisher : IMetricsPublisher
    {
        public void Publish(DateTime timeStampUtc, string metricNamespace, string metricName, long data, bool skipIfZero = true)
        {
            if (data == 0 && skipIfZero)
            {
                return;
            }

            // TODO: log the event
            Console.WriteLine($"MDM {timeStampUtc} {metricNamespace} {metricName} {data}");
        }
    }
}
