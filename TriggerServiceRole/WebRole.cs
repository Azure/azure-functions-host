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
            CloudStorageAccount account = CloudStorageAccount.DevelopmentStorageAccount;
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("triggerservice");
            CloudBlob blob = container.GetBlobReference("store.txt");

            SharedState.Init(blob);
        }
    }



    // Between front-end (HTTP listeners) and back-end (WorkerRole)
    // Accessed from multiple threads!

    // These methods on state can be accessed by front-end HTTP listeners, and so can come in on any thread. 
    public interface IFrontEndSharedState
    {
        void AddTriggers(string scope, Trigger[] triggers);

        string GetLog();
    }

    public class SharedState : IFrontEndSharedState
    {
        volatile static SharedState _value;


        public ITriggerMap _innerMap;
        public CloudBlob _blob; // where map is persisted.

        StringWriter _stringWriter; // underlying backing storage
        TextWriter _writer; // threadsafe access

        public static void Init(CloudBlob blob)
        {
            SharedState state = new SharedState
            {
                _blob = blob,
            };
            state._innerMap = state.Load();

            state._stringWriter = new StringWriter();
            state._writer = TextWriter.Synchronized(state._stringWriter);

            // this publishes the storage; now other threads can access it. 
            _value = state;
        }

        // Called from any thread.
        public static IFrontEndSharedState GetState()
        {
            var x = _value;
            if (x == null)
            {
                // If not available yet, then fail with Server not ready. 
                throw new HttpException(503, "Server is not yet initialized. Try again later");
            }
            return x;
        }

        string IFrontEndSharedState.GetLog()
        {
            // !! Thread safety?
            var x = _stringWriter.ToString();
            return x;
        }

        // Called by HTTP front-ends when receiving new triggers
        void IFrontEndSharedState.AddTriggers(string scope, Trigger[] triggers)
        {
            lock (this)
            {
                _innerMap = Load();
                _innerMap.AddTriggers(scope, triggers);
                Save();
            }

            // Main thread notice the changes in between polls.             
        }

        // Called under a lock. 
        private void Save()
        {
            string content = JsonConvert.SerializeObject(_innerMap, JsonCustom.SerializerSettings);
            _blob.UploadText(content);
        }

        [DebuggerNonUserCode]
        static string GetBlobContents(CloudBlob blob)
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

        private ITriggerMap Load()
        {
            string content = GetBlobContents(_blob);
            if (content != null)
            {
                var result = JsonConvert.DeserializeObject<TriggerMap>(content, JsonCustom.SerializerSettings);
                return result;
            }
            else
            {
                return new TriggerMap(); // empty 
            }
        }


        public void Work()
        {
            Listener l = null;
            while (true)
            {
                ITriggerMap map = null;
                lock (this)
                {
                    if (_innerMap != null)
                    {
                        // Rebuild the listener
                        map = _innerMap;
                        _innerMap = null;
                        l = null;
                    }
                }

                if (l == null)
                {
                    var logger = new WebInvokeLogger();
                    l = new Listener(map, logger);
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