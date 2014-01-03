using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs
{
    // Despite the name, this is not an IOC container.
    // This provides a global view of the distributed application (service, webpage, logging, tooling, etc)
    // Anything that needs an azure endpoint can go here.
    // This access the raw settings (especially account name) from Secrets, but then also provides the
    // policy and references to stitch everything together.
    internal class Services
    {
        private readonly IAccountInfo _accountInfo;
        private readonly CloudStorageAccount _account;

        public Services(IAccountInfo accountInfo)
        {
            _accountInfo = accountInfo;
            _account = CloudStorageAccount.Parse(accountInfo.AccountConnectionString);
        }

        public CloudStorageAccount Account
        {
            get { return _account; }
        }

        public string AccountConnectionString
        {
            get { return _accountInfo.AccountConnectionString; }
        }

        public IAccountInfo AccountInfo
        {
            get { return _accountInfo; }
        }

        public void PostDeleteRequest(FunctionInvokeRequest instance)
        {
            BlobClient.WriteBlob(_account, "abort-requests", instance.Id.ToString(), "delete requested");
        }

        public bool IsDeleteRequested(Guid instanceId)
        {
            bool x = BlobClient.DoesBlobExist(_account, "abort-requests", instanceId.ToString());
            return x;
        }

        public void LogFatalError(string message, Exception e)
        {
            StringWriter sw = new StringWriter();
            sw.WriteLine(message);
            sw.WriteLine(DateTime.Now);

            while (e != null)
            {
                sw.WriteLine("Exception:{0}", e.GetType().FullName);
                sw.WriteLine("Message:{0}", e.Message);
                sw.WriteLine(e.StackTrace);
                sw.WriteLine();
                e = e.InnerException;
            }

            string path = @"service.error/" + Guid.NewGuid().ToString() + ".txt";
            BlobClient.WriteBlob(_account, EndpointNames.ConsoleOuputLogContainerName, path, sw.ToString());
        }

        public void QueueIndexRequest(IndexRequestPayload payload)
        {
            string json = JsonCustom.SerializeObject(payload);
            GetOrchestratorControlQueue().AddMessage(new CloudQueueMessage(json));
        }

        public CloudQueue GetOrchestratorControlQueue()
        {
            CloudQueueClient client = _account.CreateCloudQueueClient();
            var queue = client.GetQueueReference(EndpointNames.OrchestratorControlQueue);
            queue.CreateIfNotExist();
            return queue;
        }

        // Important to get a quick, estimate of the depth of the execution queu.
        public int? GetExecutionQueueDepth()
        {
            var q = GetExecutionQueue();
            return q.RetrieveApproximateMessageCount();
        }

        public CloudQueue GetExecutionQueue()
        {
            CloudQueueClient queueClient = _account.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference(EndpointNames.ExecutionQueueName);
            queue.CreateIfNotExist();
            return queue;
        }

        // @@@ Remove this, move to be Ninject based. 
        public IFunctionTable GetFunctionTable()
        {
            IAzureTable<FunctionDefinition> table = new AzureTable<FunctionDefinition>(_account, EndpointNames.FunctionIndexTableName);

            return new FunctionTable(table);
        }

        public IRunningHostTableWriter GetRunningHostTableWriter()
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(_account, EndpointNames.RunningHostTableName);

            return new RunningHostTableWriter(table);
        }

        public IRunningHostTableReader GetRunningHostTableReader()
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(_account, EndpointNames.RunningHostTableName);

            return new RunningHostTableReader(table);
        }

        public AzureTable<BinderEntry> GetBinderTable()
        {
            return new AzureTable<BinderEntry>(_account, EndpointNames.BindersTableName);
        }

        public CloudBlobContainer GetExecutionLogContainer()
        {
            CloudBlobClient client = _account.CreateCloudBlobClient();
            CloudBlobContainer c = client.GetContainerReference(EndpointNames.ConsoleOuputLogContainerName);
            c.CreateIfNotExist();
            var permissions = c.GetPermissions();

            // Set private access to avoid leaking data.
            if (permissions.PublicAccess != BlobContainerPublicAccessType.Off)
            {
                permissions.PublicAccess = BlobContainerPublicAccessType.Off;
                c.SetPermissions(permissions);
            }
            return c;
        }

        // $$$ Returning bundles of interfaces... this is really looking like we need IOC.
        // Similar bundle with FunctionExecutionContext
        public QueueInterfaces GetQueueInterfaces()
        {
            var x = GetFunctionUpdatedLogger();

            return new QueueInterfaces
            {
                AccountInfo = _accountInfo,
                Logger = x,
                Lookup = x,
                CausalityLogger = GetCausalityLogger(),
                PrereqManager = GetPrereqManager(x)
            };
        }

        private CloudBlobContainer GetHealthLogContainer()
        {
            CloudBlobClient client = _account.CreateCloudBlobClient();
            CloudBlobContainer c = client.GetContainerReference(EndpointNames.HealthLogContainerName);
            c.CreateIfNotExist();
            return c;
        }

        [DebuggerNonUserCode]
        public ServiceHealthStatus GetHealthStatus()
        {
            var stats = new ServiceHealthStatus();

            stats.Executors = new Dictionary<string, ExecutionRoleHeartbeat>();

            BlobRequestOptions opts = new BlobRequestOptions { UseFlatBlobListing = true };
            foreach (CloudBlob blob in GetHealthLogContainer().ListBlobs(opts))
            {
                try
                {
                    string json = blob.DownloadText();
                    if (blob.Name.StartsWith(@"orch", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.Orchestrator = JsonCustom.DeserializeObject<OrchestratorRoleHeartbeat>(json);
                    }
                    else
                    {
                        stats.Executors[blob.Name] = JsonCustom.DeserializeObject<ExecutionRoleHeartbeat>(json);
                    }
                }
                catch (StorageClientException)
                {
                }
                catch (JsonSerializationException)
                {
                    // Ignore serialization errors. This is just health status. 
                }
            }

            return stats;
        }

        public void WriteHealthStatus(string role, ExecutionRoleHeartbeat status)
        {
            string content = JsonCustom.SerializeObject(status);
            GetHealthLogContainer().GetBlobReference(@"exec/" + role + ".txt").UploadText(content);
        }

        public IPrereqManager GetPrereqManager()
        {
            IFunctionInstanceLookup lookup = GetFunctionInstanceLookup();
            return GetPrereqManager(lookup);
        }

        public IPrereqManager GetPrereqManager(IFunctionInstanceLookup lookup)
        {
            IAzureTable prereqTable = new AzureTable(_account, "schedPrereqTable");
            IAzureTable successorTable = new AzureTable(_account, "schedSuccessorTable");

            return new PrereqManager(prereqTable, successorTable, lookup);
        }

        public ICausalityLogger GetCausalityLogger()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, EndpointNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = null; // write-only mode
            return new CausalityLogger(table, logger);
        }

        public ICausalityReader GetCausalityReader()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, EndpointNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = this.GetFunctionInstanceLookup(); // read-mode
            return new CausalityLogger(table, logger);
        }

        public FunctionUpdatedLogger GetFunctionUpdatedLogger()
        {
            var table = new AzureTable<ExecutionInstanceLogEntity>(_account, EndpointNames.FunctionInvokeLogTableName);
            return new FunctionUpdatedLogger(table);
        }

        // Streamlined case if we just need to lookup specific function instances.
        // In this case, we don't need all the secondary indices.
        public IFunctionInstanceLookup GetFunctionInstanceLookup()
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = GetFunctionLookupTable();
            return new ExecutionStatsAggregator(tableLookup);
        }

        public IFunctionInstanceQuery GetFunctionInstanceQuery()
        {
            return GetStatsAggregatorInternal();
        }

        public IFunctionInstanceLogger GetFunctionInstanceLogger()
        {
            return GetStatsAggregatorInternal();
        }

        private ExecutionStatsAggregator GetStatsAggregatorInternal()
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = GetFunctionLookupTable();
            var tableStatsSummary = GetInvokeStatsTable();
            var tableMru = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMru);
            var tableMruByFunction = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMruFunction);
            var tableMruByFunctionSucceeded = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMruFunctionSucceeded);
            var tableMruFunctionFailed = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMruFunctionFailed);

            return new ExecutionStatsAggregator(
                tableLookup,
                tableStatsSummary,
                tableMru,
                tableMruByFunction,
                tableMruByFunctionSucceeded,
                tableMruFunctionFailed);
        }

        // Table that maps function types to summary statistics. 
        // Table is populated by the ExecutionStatsAggregator
        public AzureTable<FunctionLocation, FunctionStatsEntity> GetInvokeStatsTable()
        {
            return new AzureTable<FunctionLocation, FunctionStatsEntity>(
                _account,
                EndpointNames.FunctionInvokeStatsTableName,
                 row => Tuple.Create("1", row.ToString()));
        }

        private IAzureTable<FunctionInstanceGuid> GetIndexTable(string tableName)
        {
            return new AzureTable<FunctionInstanceGuid>(_account, tableName);
        }

        private IAzureTableReader<ExecutionInstanceLogEntity> GetFunctionLookupTable()
        {
            return new AzureTable<ExecutionInstanceLogEntity>(
                  _account,
                  EndpointNames.FunctionInvokeLogTableName);
        }
    }
}
