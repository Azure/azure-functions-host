using System.Text;

namespace Microsoft.WindowsAzure.Jobs
{
    // Ping a webservice to notify that there's a new blob. 
    internal class NotifyNewBlobViaWebApi : INotifyNewBlob
    {
        private readonly string _serviceUrl;

        public NotifyNewBlobViaWebApi(string serviceUrl)
        {
            this._serviceUrl = serviceUrl;
        }

        public void Notify(BlobWrittenMessage msg)
        {
            // $$$ What about escaping and encoding?
            StringBuilder sb = new StringBuilder();
            sb.Append(_serviceUrl);
            sb.Append("/api/execution/NotifyBlob");

            try
            {
                Web.PostJson(sb.ToString(), msg);
            }
            catch
            {
                // Ignorable. 
            }
        }
    }
}
