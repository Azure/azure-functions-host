namespace Microsoft.Azure.WebJobs.Script.Description
{
    public enum BindingType
    {
        Queue,
        QueueTrigger,
        Blob,
        BlobTrigger,
        ServiceBus,
        ServiceBusTrigger,
        HttpTrigger,
        Http,
        Table,
        ManualTrigger,
        TimerTrigger
    }
}
