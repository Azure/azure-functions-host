namespace Microsoft.WindowsAzure.Jobs.Storage.Queues
{
    internal interface ICloudQueue
    {
        void AddMessage(ICloudQueueMessage message);

        void CreateIfNotExists();

        ICloudQueueMessage CreateMessage(string content);
    }
}
