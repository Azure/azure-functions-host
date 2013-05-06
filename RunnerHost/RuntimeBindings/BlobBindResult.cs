using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Wraps a bind Result and records the blob's authoring function after the blob is written.
    // This preserves causality functionality. 
    class BlobBindResult : BindResult
    {
        private readonly BindResult _inner;
        private readonly Guid _functionWriter;
        private readonly CloudBlob _blob;
        private readonly IBlobCausalityLogger _logger;

        private BlobBindResult(BindResult inner, Guid functionWriter, CloudBlob blob, IBlobCausalityLogger logger)
        {
            _functionWriter = functionWriter;
            _blob = blob;
            _logger = logger;

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

            // This is the critical call to record causality. 
            // The entire purpose of this wrapper class is to make this call. 
            // This must be called after the blbo is written, since it may stamp the blob. 
            _logger.SetWriter(_blob, _functionWriter);
        }

        // Get a BindResult for a blob that will stamp the blob with the GUID of the function instance that wrote it. 
        public static BindResult BindWrapper(bool isInput, ICloudBlobBinder blobBinder, IBinderEx bindingContext, Type targetType, CloudBlob blob, IBlobCausalityLogger logger)
        {
            string containerName = blob.Container.Name;
            string blobName = blob.Name;

            // Invoke the inner binder to create a cloud blob. 
            var inner = blobBinder.Bind(bindingContext, containerName, blobName, targetType);
            if (isInput)
            {
                // Only stamp blobs we write. 
                return inner;
            }

            // Now wrap it with a result that will tag it with a Guid. 
            Guid functionWriter = bindingContext.FunctionInstanceGuid;
            return new BlobBindResult(inner, functionWriter, blob, logger);
        }
    }
}