using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using TriggerService;

namespace TriggerServiceRole
{
    public class TriggerConfig
    {
        private CloudStorageAccount _account;

        // Error string describing startup failures. Useful for diagnostics. 
        private string _error;

        private CloudBlob _blob; // where map is persisted.

        public TriggerConfig()
        {
            try
            {
                var val = RoleEnvironment.GetConfigurationSettingValue("MainStorage");
                _account = CloudStorageAccount.Parse(val);
            }
            catch (Exception e)
            {
                _error = "Couldn't initialize storage:" + e.Message;
            }
        }

        // Get an unstructured string summarizing the configuration. 
        public string GetConfigInfo()
        {
            if (_error != null)
            {
                return _error;
            }
            var account = this.GetAccount();
            return account.Credentials.AccountName;

        }

        private CloudStorageAccount GetAccount()
        {
            return _account;
        }

        public CloudQueue GetDeltaQueue()
        {
            CloudQueueClient client = _account.CreateCloudQueueClient();
            var q = client.GetQueueReference("triggerdeltaq");
            q.CreateIfNotExist();
            return q;
        }

        private CloudBlob GetTriggerMapBlob()
        {
            if (_blob == null)
            {
                CloudBlobClient client = this.GetAccount().CreateCloudBlobClient();
                CloudBlobContainer container = client.GetContainerReference("triggerservice");
                container.CreateIfNotExist();
                CloudBlob blob = container.GetBlobReference("store.txt");
                _blob = blob;
            }
            return _blob;
        }

        // Called under a lock. 

        public void Save(ITriggerMap map)
        {
            string content = TriggerMap.SaveJson(map);
            GetTriggerMapBlob().UploadText(content);
        }

        public ITriggerMap Load()
        {
            string content = GetBlobContents(GetTriggerMapBlob());
            if (content != null)
            {
                var result = TriggerMap.LoadJson(content);
                return result;
            }
            else
            {
                return new TriggerMap(); // empty 
            }
        }

        [DebuggerNonUserCode]
        private static string GetBlobContents(CloudBlob blob)
        {
            try
            {
                string content = blob.DownloadText();
                return content;
            }
            catch
            {
                return null; // not found
            }
        }    
    }
}