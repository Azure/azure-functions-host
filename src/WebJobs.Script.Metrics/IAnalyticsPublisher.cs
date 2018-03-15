using System;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public interface IAnalyticsPublisher
    {
        void WriteEvent(string siteName = null,
            string feature = null,
            string objectTypes = null,
            string objectNames = null,
            string dataKeys = null,
            string dataValues = null,
            string action = null,
            DateTime? actionTimeStamp = null,
            bool succeeded = true);

        void WriteError(int processId, string containerName, string message, string details);
    }
}
