namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusTrigger : Trigger
    {
        public ServiceBusTrigger()
        {
            this.Type = TriggerType.ServiceBus;
        }

        public string SourcePath { get; set; }

        public override string ToString()
        {
            return string.Format("Trigger on service bus '{0}'", SourcePath);
        }
    }
}