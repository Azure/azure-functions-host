namespace Microsoft.WindowsAzure.Jobs.Storage.Queues
{
    internal interface ICloudQueueClient
    {
        ICloudQueue GetQueueReference(string queueName);
    }
}
