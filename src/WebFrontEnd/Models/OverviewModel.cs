using Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Controllers
{
    public class OverviewModel
    {
        public string AccountName { get; set; }
        public string ExecutionSubstrate { get; set; }
        public string VersionInformation { get; set; }

        public int? QueueDepth { get; set; }

        public ServiceHealthStatusModel HealthStatus { get; set; }
    }
}
