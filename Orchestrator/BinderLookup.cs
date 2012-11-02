using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace Orchestrator
{
    class BinderLookup
    {
        // Table mapping Type names to model binder locations. 
        // The locations all live _account
        private readonly IAzureTableReader<BinderEntry> _table;

        // Where it's copied to locally
        // We'll download and write back here, and then the caller can do a smart upload.
        private readonly string _localCache;

        private readonly List<ModelBinderManifest.Entry> _list = new List<ModelBinderManifest.Entry>();

        public BinderLookup(IAzureTableReader<BinderEntry> table, string localCache)
        {
            _table = table;
            _localCache = localCache;
        }

        // Return true if resolved, else false
        public bool Lookup(Type t)
        {
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
