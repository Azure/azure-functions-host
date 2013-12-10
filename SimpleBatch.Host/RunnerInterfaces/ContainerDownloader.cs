using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // $$$ Use elsewhere?
    // helper for downloading a container to a local directory.
    // - copies to a unique private subdirectory of the root to avoid interfering with other uses.
    // - uploads any new files that were added.
    // - deletes on finish
    public class ContainerDownloader : IDisposable
    {
        string _localCachePrivate;
        public string LocalCachePrivate { get { return _localCachePrivate; } }

        // If the local cache has any new files, then copy then back to the 
        private HashSet<string> _copyBack;
        CloudBlobDescriptor _containerDescriptor;

        public ContainerDownloader(CloudBlobDescriptor containerDescriptor, string localCacheRoot, bool uploadNewFiles = false)
        {
            _containerDescriptor = containerDescriptor;
            _localCachePrivate = GetUniqueCache(localCacheRoot);

            var container = containerDescriptor.GetContainer();
            LocalCopy(container, _localCachePrivate);

            if (uploadNewFiles)
            {
                _copyBack = new HashSet<string>(Directory.EnumerateFiles(_localCachePrivate));
            }
        }

        public void Dispose()
        {
            if (_copyBack != null)
            {
                // Upload any new files back to the cloud.
                var container = _containerDescriptor.GetContainer();

                foreach (var file in Directory.EnumerateFiles(_localCachePrivate))
                {
                    if (!_copyBack.Contains(file))
                    {
                        string shortname = Path.GetFileName(file);
                        var blob = container.GetBlobReference(shortname);
                        blob.UploadFile(file);
                    }
                }
            }

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