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
        void QueueAddTriggerRequest(string scope, AddTriggerPayload triggers);

        string GetConfigInfo();

        ITriggerMap GetMap();
    }
    
    class AddTriggerQueuePayload
    {
        public string Scope { get; set; }
        public AddTriggerPayload Triggers { get; set; }
    }

    public class FrontEnd : IFrontEndSharedState
    {
        TriggerConfig _config = new TriggerConfig();

        public string GetConfigInfo()
        {
            return _config.GetConfigInfo();
        }

        public void QueueAddTriggerRequest(string scope, AddTriggerPayload triggers)
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

        public ITriggerMap GetMap()
        {
            return _config.Load();
        }
    }
}
