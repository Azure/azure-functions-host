using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebJobs.Script.WebHost
{
    public class FunctionInvocationManager
    {
        private readonly ScriptHostManager _scriptHostManager;
        private readonly TraceWriter _traceWriter;
        private readonly CloudStorageAccount _storageAccount;
        private CloudQueue _hostMessageQueue;

        public FunctionInvocationManager(ScriptHostManager scriptHostManager, string storageConnectionString, TraceWriter traceWriter)
        {
            _scriptHostManager = scriptHostManager;
            _traceWriter = traceWriter;
            _storageAccount = CloudStorageAccount.Parse(storageConnectionString);
        }

        private CloudQueue HostMessageQueue
        {
            get
            {
                if (_hostMessageQueue == null)
                {
                    CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
                    string hostId = _scriptHostManager.Instance.ScriptConfig.HostConfig.HostId;
                    string hostQueueName = string.Format("azure-webjobs-host-{0}", hostId);
                    _hostMessageQueue = queueClient.GetQueueReference(hostQueueName);
                    _hostMessageQueue.CreateIfNotExists();
                }
                return _hostMessageQueue;
            }
        }

        public void Enqueue(Guid id, string functionName, string input, ParameterDescriptor inputParameter)
        {
            string functionId = string.Format("Host.Functions.{0}", functionName);
            JObject argumentsObject = new JObject()
            {
                { inputParameter.Name, input }
            };
            
            JObject message = new JObject()
            {
                { "Type", "CallAndOverride" },
                { "Id", id.ToString() },
                { "FunctionId", functionId.ToString() },
                { "Arguments", argumentsObject }
            };

            CloudQueueMessage queueMessage = new CloudQueueMessage(message.ToString(Formatting.None));
            HostMessageQueue.AddMessage(queueMessage);
        }
    }
}