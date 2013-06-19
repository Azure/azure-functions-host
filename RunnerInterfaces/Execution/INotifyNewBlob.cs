using System.Text;

namespace RunnerInterfaces
{
    public class BlobWrittenMessage
    {
        public string AccountName { get; set; }
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
    }

    // Listening to external blobs is slow. But when we write a blob ourselves, we can hook the notifations
    // so that we detect the new blob immediately without polling.
    public interface INotifyNewBlob
    {
        void Notify(string accountName, string containerName, string blobName);
    }

    public class NotifyNewBlob : INotifyNewBlob
    {
        private readonly string _serviceUrl;

        public NotifyNewBlob(string serviceUrl)
        {
            this._serviceUrl = serviceUrl;
        }

        public void Notify(string accountName, string containerName, string blobName)
        {
            BlobWrittenMessage msg = new BlobWrittenMessage
            {
                AccountName = accountName,
                BlobName = blobName,
                ContainerName = containerName
            };

            // $$$ What about escaping and encoding?
            StringBuilder sb = new StringBuilder();
            sb.Append(_serviceUrl);
            sb.Append("/api/execution/NotifyBlob");

            try
            {
                Utility.PostJson(sb.ToString(), msg);
            }
            catch
            {
                // Ignorable. 
            }
        }
    }
}