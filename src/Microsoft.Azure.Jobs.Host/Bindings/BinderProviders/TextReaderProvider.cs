using System;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    class TextReaderProvider : ICloudBlobBinderProvider
    {
        private class TextReaderBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                ICloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                long length = blob.Properties.Length;
                var blobStream = blob.OpenRead();
                var streamWatcher = new WatchableStream(blobStream, length);
                var textReader = new StreamReader(streamWatcher);

                return new BindCleanupResult
                {
                    Result = textReader,
                    SelfWatch = streamWatcher, 
                    Cleanup = () => textReader.Close()
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(TextReader))
            {
                return new TextReaderBinder();
            }
            return null;
        }
    }
}
