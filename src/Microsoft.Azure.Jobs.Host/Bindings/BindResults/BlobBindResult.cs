using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Wraps a bind Result and records the blob's authoring function after the blob is written.
    // This preserves causality functionality. 
    class BlobBindResult : BindResult
    {
        private readonly BindResult _inner;
        private readonly Guid _functionWriter;
        private readonly ICloudBlob _blob;
        private readonly IBlobCausalityLogger _logger;
        private readonly INotifyNewBlob _notify;

        private BlobBindResult(BindResult inner, Guid functionWriter, ICloudBlob blob, IBlobCausalityLogger logger, INotifyNewBlob notify)
        {
            _functionWriter = functionWriter;
            _blob = blob;
            _logger = logger;
            _notify = notify;

            _inner = inner;
            this.Result = _inner.Result;
        }

        public override ISelfWatch Watcher
        {
            get
            {
                return _inner.Watcher;
            }
        }

        public override void OnPostAction()
        {
            _inner.Result = this.Result;
            _inner.OnPostAction(); // important, this is what may write the blob.

            if (BlobClient.DoesBlobExist(_blob))
            {
                // This is the critical call to record causality. 
                // The entire purpose of this wrapper class is to make this call. 
                // This must be called after the blob is written, since it may stamp the blob. 
                _logger.SetWriter(_blob, _functionWriter);

                // Notify that blob is available. 
                if (_notify != null)
                {
                    string name = _blob.ServiceClient.Credentials.AccountName;
                    _notify.Notify(name, _blob.Container.Name, _blob.Name);
                }
            }
        }

        // Get a BindResult for a blob that will stamp the blob with the GUID of the function instance that wrote it. 
        public static BindResult BindWrapper(ICloudBlobBinder blobBinder, IBinderEx bindingContext, Type targetType, ICloudBlob blob, IBlobCausalityLogger logger)
        {
            if (blob == null)
            {
                return new NullBindResult("Input blob was not found");
            }

            string containerName = blob.Container.Name;
            string blobName = blob.Name;

            // Invoke the inner binder to create a cloud blob. 
            var inner = blobBinder.Bind(bindingContext, containerName, blobName, targetType);

            INotifyNewBlob notify = bindingContext.NotifyNewBlob;

            // Now wrap it with a result that will tag it with a Guid. 
            Guid functionWriter = bindingContext.FunctionInstanceGuid;
            return new BlobBindResult(inner, functionWriter, blob, logger, notify);
        }
    }
}
