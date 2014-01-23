using System;
using System.IO;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    class StringBlobBinderProvider : ICloudBlobBinderProvider
    {
        private class StringBlobInputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                string result;

                using (var blobStream = blob.OpenRead())
                using (var textReader = new StreamReader(blobStream))
                {
                    result = textReader.ReadToEnd();
                }

                return new BindResult
                {
                    Result = result
                };
            }
        }

        private class StringBlobOutputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                var result = new BindCleanupResult();
                result.Cleanup = () =>
                    {
                        string content = (string)result.Result;
                        if (content != null)
                        {
                            blob.UploadText(content);
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
