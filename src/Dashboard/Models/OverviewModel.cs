using Dashboard.Models.Protocol;

namespace Dashboard.Controllers
{
    public class OverviewModel
    {
        public string AccountName { get; set; }

        public string VersionInformation { get; set; }

        public int? QueueDepth { get; set; }

        public ServiceHealthStatusModel HealthStatus { get; set; }
    }
}
