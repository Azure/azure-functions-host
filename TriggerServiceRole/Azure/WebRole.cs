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
    // Entry point invoked for Azure.
    // http://blog.liamcavanagh.com/2011/12/how-to-combine-a-worker-role-with-a-mvc4-web-role-into-a-single-instance
    public class WebWorker : Microsoft.WindowsAzure.ServiceRuntime.RoleEntryPoint
    {
        public override bool OnStart()
        {
            Work();
            return base.OnStart();
        }

        private void Work()
        {
            // RoleEnvironment.GetConfigurationSettingValue("Storage");
            var state = new SharedState(new TriggerConfig());
            state.Work();
        }
    }



    public class SharedState
    {
        private readonly TriggerConfig _config;

        StringWriter _stringWriter; // underlying backing storage
        TextWriter _writer; // threadsafe access

        private readonly CloudQueue _requestQ;

        public SharedState(TriggerConfig config)
        {
            _config = config;
            _requestQ = config.GetDeltaQueue();

            _stringWriter = new StringWriter();
            _writer = TextWriter.Synchronized(_stringWriter);
        }

        public void Work()
        {
            var q = this._requestQ;

            ITriggerMap map = _config.Load();
                        
            Listener l = null;
            bool resetListner = false;

            while (true)
            {
                // Apply any changes.            
                while (true)
                {
                    var msg = q.GetMessage();
                    if (msg == null)
                    {
                        break;
                    }
                    var payload = JsonConvert.DeserializeObject<AddTriggerQueuePayload>(msg.AsString);
                    var x = payload.Triggers;
                    var triggers = Trigger.FromWire(x.Triggers, x.Credentials).ToArray();
                    
                    // Assumes single-threaded
                    map.AddTriggers(payload.Scope, triggers);
                    _config.Save(map);
                    resetListner = true;

                    q.DeleteMessage(msg);
                }

                if (resetListner)
                {
                    if (l != null)
                    {
                        l.Dispose();
                    }
                    l = null;
                }

                if (l == null)
                {
                    l = new Listener(map, new WebInvoke ());
                }


                l.Poll();
                Thread.Sleep(2 * 1000);
            }
        }
    }


    class WebInvokeLogger : ITriggerInvoke
    {
        public TextWriter _writer;

        public void OnNewTimer(TimerTrigger func, CancellationToken token)
        {
            _writer.WriteLine("Timer invoked @ {0}", func.Interval);
        }

        public void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, CancellationToken token)
        {
            _writer.WriteLine("New queue invoked {0}, {1}", func.QueueName, msg.AsString);
        }

        public void OnNewBlob(CloudBlob blob, BlobTrigger func, CancellationToken token)
        {
            _writer.WriteLine("New blob input detected {0}", func.BlobInput);
        }
    }

    static class JsonCustom
    {
        public static JsonSerializerSettings NewSettings()
        {
            var settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            return settings;
        }

        public static JsonSerializerSettings _settings = NewSettings();

        public static JsonSerializerSettings SerializerSettings
        {
            get
            {
                return _settings;
            }
        }
    }
}