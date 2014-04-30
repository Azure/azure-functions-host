using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    class StringBlobBinderProvider : ICloudBlobBinderProvider
    {
        private class StringBlobInputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                ICloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                string result;
                string readMessage;

                using (var blobStream = blob.OpenRead())
                using (var watchable = new WatchableStream(blobStream, blob.Properties.Length))
                using (var textReader = new StreamReader(watchable))
                {
                    result = textReader.ReadToEnd();
                    readMessage = watchable.GetStatus();
                }

                var watcher = new SimpleWatcher();
                watcher.SetStatus(readMessage);

                return new BindCleanupResult
                {
                    Result = result,
                    SelfWatch = watcher
                };
            }
        }

        private class StringBlobOutputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                ICloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);
                
                var result = new BindCleanupResult();
                var watcher = new SimpleWatcher();
                result.SelfWatch = watcher;
                result.Cleanup = () =>
                    {
                        string content = (string)result.Result;
                        if (content != null)
                        {
                            var bytes = Encoding.UTF8.GetBytes(content);
                            blob.UploadFromByteArray(bytes, 0, bytes.Length);
                            var status = bytes.Length == 0
                                ? "Written empty file."
                                : string.Format(CultureInfo.CurrentCulture, 
                                    "Written {0} bytes.",
                                    bytes.Length);
                            watcher.SetStatus(status);
                        }
                    };
                return result;
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(string))
            {
                if (isInput)
                {
                    return new StringBlobInputBinder();
                }
                else
                {
                    return new StringBlobOutputBinder();
                }
            }
            return null;
        }
    }
}
