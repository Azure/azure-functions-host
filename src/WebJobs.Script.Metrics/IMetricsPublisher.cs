using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public interface IMetricsPublisher
    {
        void Publish(DateTime timeStampUtc, string metricNamespace, string metricName, long data, bool skipIfZero = true);
    }
}
