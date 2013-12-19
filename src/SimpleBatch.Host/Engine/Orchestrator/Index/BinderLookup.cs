using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    class BinderLookup
    {
        // Table mapping Type names to model binder locations. 
        // The locations all live _account
        private readonly IAzureTableReader<BinderEntry> _table;

        // Where it's copied to locally
        // We'll download and write back here, and then the caller can do a smart upload.
        private readonly string _localCache;

        // Hash instead of list since we may have multiple types all pointing to the same binder. 
        private readonly HashSet<ModelBinderManifest.Entry> _list = new HashSet<ModelBinderManifest.Entry>();

        public BinderLookup(IAzureTableReader<BinderEntry> table, string localCache)
        {
            _table = table;
            _localCache = localCache;
        }

        // Return true if resolved, else false
        public bool Lookup(Type t)
        {
            // If they ask for a generic interface, look for the definition. 
            // Binder is responsible for handling generics. 
            if (t.IsGenericType)
            {
                t = t.GetGenericTypeDefinition();
            }

            string key = t.FullName;

            var entry = _table.Lookup("1", key);
            if (entry == null)
            {
                // Not found
                return false;
            }

            CloudStorageAccount account = Utility.GetAccount(entry.AccountConnectionString);

            // Copy down.
            // $$$ This is a huge deal. What about version conflicts? Or naming conflicts? 
            // Caller is responsible for detecting the new files and uploading them back to the Cloud .
            foreach (CloudBlob blob in entry.Path.ListBlobsInDir(account))
            {
                string name = Path.GetFileName(blob.Name); // $$$ Assumes flat directory 
                string localPath = Path.Combine(_localCache, name);
                if (!File.Exists(localPath))
                {
                    blob.DownloadToFile(localPath);
                }
            }

            _list.Add(new ModelBinderManifest.Entry { AssemblyName = entry.InitAssembly, TypeName = entry.InitType });

            return true;
        }

        public void WriteManifest(string filename)
        {
            var manifest = new ModelBinderManifest
            {
                Entries = _list.ToArray()
            };
            var json = JsonCustom.SerializeObject(manifest);

            File.WriteAllText(Path.Combine(_localCache, filename), json);
        }
    }
}
