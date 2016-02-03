namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class BindingMetadata
    {
        public string Name { get; set; }

        public BindingType Type { get;set; }

        public bool IsTrigger
        {
            get
            {
                return
                    Type == BindingType.TimerTrigger ||
                    Type == BindingType.BlobTrigger ||
                    Type == BindingType.HttpTrigger ||
                    Type == BindingType.QueueTrigger ||
                    Type == BindingType.ServiceBusTrigger ||
                    Type == BindingType.ManualTrigger;
            }
        }
    }
}
