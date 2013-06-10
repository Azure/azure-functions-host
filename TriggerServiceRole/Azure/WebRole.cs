using System;
using System.Linq;
using System.Threading;
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
            return base.OnStart();
        }

        public override void Run()
        {
            Work();
        }

        private void Work()
        {
            var config = new TriggerConfig();
            try
            {
                var state = new SharedState(config);
                state.Work();
            }
            catch (Exception e)
            {
                config.LogFatalError(e);
            }
        }
    }



    public class SharedState
    {
        private readonly TriggerConfig _config;

        private readonly CloudQueue _requestQ;

        public SharedState(TriggerConfig config)
        {
            _config = config;
            _requestQ = config.GetDeltaQueue();
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
                    resetListner = false;
                    if (l != null)
                    {
                        l.Dispose();
                    }
                    l = null;
                }

                if (l == null)
                {
                    l = new Listener(map, new LoggingWebInvoke());
                }


                l.Poll();
                Thread.Sleep(60 * 1000);
            }
        }
    }
}