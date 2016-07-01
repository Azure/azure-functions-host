using System.Collections.Generic;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class ArmSubscriptionsArray
    {
        public IEnumerable<ArmSubscription> value { get; set; }
    }
}