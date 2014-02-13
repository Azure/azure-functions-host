namespace Microsoft.WindowsAzure.Jobs.Host.Storage.Queue
{
    internal interface ICloudQueueClient
    {
        ICloudQueue GetQueueReference(string queueName);
    }
}
