namespace Microsoft.Azure.Jobs.Host.Storage.Queue
{
    internal interface ICloudQueue
    {
        void AddMessage(ICloudQueueMessage message);

        void CreateIfNotExists();

        ICloudQueueMessage CreateMessage(string content);
    }
}
