using Microsoft.Azure.WebJobs.Script.Metrics;
using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxContainerAnalyticsPublisher : IAnalyticsPublisher
    {
        public void WriteEvent(string siteName = null,
            string feature = null,
            string objectTypes = null,
            string objectNames = null,
            string dataKeys = null,
            string dataValues = null,
            string action = null,
            DateTime? actionTimeStamp = null,
            bool succeeded = true)
        {
            // TODO: write event
        }

        public void WriteError(int processId, string containerName, string message, string details)
        {
            // TODO: write error
        }
    }
}
