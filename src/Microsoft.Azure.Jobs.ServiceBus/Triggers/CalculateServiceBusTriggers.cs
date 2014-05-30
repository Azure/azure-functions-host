using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.Azure.Jobs.ServiceBus.Triggers;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class CalculateServiceBusTriggers
    {
        public static TriggerRaw GetTriggerRaw(ITriggerBinding triggerBinding)
        {
            ServiceBusTriggerBinding serviceBusTriggerBinding = triggerBinding as ServiceBusTriggerBinding;

            if (serviceBusTriggerBinding == null)
            {
                return null;
            }

            return TriggerRaw.NewServiceBus(serviceBusTriggerBinding.EntityPath);
        }
    }
}