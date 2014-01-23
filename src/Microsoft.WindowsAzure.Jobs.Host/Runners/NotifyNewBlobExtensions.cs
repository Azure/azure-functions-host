namespace Microsoft.WindowsAzure.Jobs
{
    internal static class NotifyNewBlobExtensions
    {
        public static void Notify(this INotifyNewBlob x, string accountName, string containerName, string blobName)
        {
            BlobWrittenMessage msg = new BlobWrittenMessage
            {
                AccountName = accountName,
                BlobName = blobName,
                ContainerName = containerName
            };
            x.Notify(msg);
        }
    }
}
