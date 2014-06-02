using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class BlobCommittedAction : IBlobCommitedAction
    {
        private readonly ICloudBlob _blob;
        private readonly Guid _functionInstanceId;
        private readonly INotifyNewBlob _notify;

        public BlobCommittedAction(ICloudBlob blob, Guid functionInstanceId, INotifyNewBlob notify)
        {
            _blob = blob;
            _functionInstanceId = functionInstanceId;
            _notify = notify;
        }

        public void Execute()
        {
            // This is the critical call to record causality. 
            // This must be called after the blob is written, since it may stamp the blob. 
            BlobCausalityLogger.SetWriter(_blob, _functionInstanceId);

            // Notify that blob is available. 
            if (_notify != null)
            {
                string accountName = _blob.ServiceClient.Credentials.AccountName;
                _notify.Notify(accountName, _blob.Container.Name, _blob.Name);
            }
        }
    }
}
