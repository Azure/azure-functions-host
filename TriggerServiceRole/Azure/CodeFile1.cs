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
    // These methods on state can be accessed by front-end HTTP listeners, and so can come in on any thread. 
    public interface IFrontEndSharedState
    {
        void AddTriggers(string scope, AddTriggerPayload triggers);

        string GetLog();
    }


    class AddTriggerQueuePayload
    {
        public string Scope { get; set; }
        public AddTriggerPayload Triggers { get; set; }
    }

    public class FrontEnd : IFrontEndSharedState
    {
        TriggerConfig _config = new TriggerConfig();

        public void AddTriggers(string scope, AddTriggerPayload triggers)
        {
            var payload = new AddTriggerQueuePayload 
            { 
                Scope = scope,
                Triggers = triggers
            };

            var json = JsonConvert.SerializeObject(payload);
            var msg = new CloudQueueMessage(json);
            var q = _config.GetDeltaQueue();
            q.AddMessage(msg);            
        }

        public string GetLog()
        {
            throw new NotImplementedException();
        }
    }

    public class TriggerConfig
    {
        CloudStorageAccount _account;

        public TriggerConfig()
        {
            // !!! Get from config
            // RoleEnvironment.GetConfigurationSettingValue("Storage");
            _account = CloudStorageAccount.DevelopmentStorageAccount;
        }

        public CloudStorageAccount GetAccount()
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
    }
}