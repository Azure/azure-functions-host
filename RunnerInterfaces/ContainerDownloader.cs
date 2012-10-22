using System;
using System.IO;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // $$$ Use elsewhere?
    // helper for downloading a container to a local directory.
    // - copies to a unique private subdirectory of the root to avoid interfering with other uses.
    // - deletes on finish
    public class ContainerDownloader : IDisposable
    {
        string _localCachePrivate;

        public string LocalCachePrivate { get { return _localCachePrivate; } }

        public ContainerDownloader(CloudBlobDescriptor containerDescriptor, string localCacheRoot)
        {
            _localCachePrivate = GetUniqueCache(localCacheRoot);

            var container = containerDescriptor.GetContainer();
            LocalCopy(container, _localCachePrivate);
        }

        public void Dispose()
        {
            Utility.DeleteDirectory(_localCachePrivate);                            
        }

        // Give a unique subdir, which this instance can own and know it won't 
        // conflict with other callers.
        private static string GetUniqueCache(string localCache)
        {
            if (!Directory.Exists(localCache))
            {
                Directory.CreateDirectory(localCache);
            }

            string localCache2 = Path.Combine(localCache, Guid.NewGuid().ToString());
            if (!Directory.Exists(localCache2))
            {
                Directory.CreateDirectory(localCache2);
            }
            return localCache2;
        }

        private void LocalCopy(CloudBlobContainer container, string localPath)
        {
            foreach (var b in container.ListBlobs())
            {
                string shortName = Path.GetFileName(b.Uri.ToString());

                var blob = container.GetBlobReference(shortName);
                string localFile = Path.Combine(localPath, shortName);
                blob.DownloadToFile(localFile);
            }
        }
    }
}