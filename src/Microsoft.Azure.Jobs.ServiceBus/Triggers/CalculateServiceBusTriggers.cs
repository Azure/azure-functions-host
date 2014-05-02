namespace Microsoft.Azure.Jobs
{
    internal class CalculateServiceBusTriggers
    {
        public static TriggerRaw GetTriggerRaw(ParameterStaticBinding input)
        {
            var serviceBusBinding = input as ServiceBusParameterStaticBinding;
            if (serviceBusBinding != null && serviceBusBinding.IsInput)
            {
                return TriggerRaw.NewServiceBus(null, serviceBusBinding.EntityPath);
            }
            return null;
        }
    }
}