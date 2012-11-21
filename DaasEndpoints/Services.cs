using System;
using System.Collections.Generic;
using System.IO;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using AzureTables;
using Microsoft.WindowsAzure.ServiceRuntime;

// Despite the name, this is not an IOC container. 
// This provides a global view of the distributed application (service, webpage, logging, tooling, etc)
// Anything that needs an azure endpoint can go here.
// This access the raw settings (especially account name) from Secrets, but then also provides the 
// policy and references to stitch everything together. 
public partial class Services
{
    // Get list of all registered functions.     
    // $$$ Merge with CloudIndexerSettings
    public static FunctionIndexEntity[] GetFunctions()
    {
        var funcs = Utility.ReadTable<FunctionIndexEntity>(Secrets.GetAccount(), Secrets.FunctionIndexTableName);
        return funcs;
    }

    public static void PostDeleteRequest(FunctionInstance instance)
    {
        Utility.WriteBlob(Secrets.GetAccount(), "abort-requests", instance.Id.ToString(), "delete requested");        
    }

    public static bool IsDeleteRequested(Guid instanceId)
    {
        bool x = Utility.DoesBlobExist(Secrets.GetAccount(), "abort-requests", instanceId.ToString());        
        return x;
    }

    public static void LogFatalError(string message, Exception e)
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

        string path = @"service.error\" + Guid.NewGuid().ToString() + ".txt";
        Utility.WriteBlob(Secrets.GetAccount(), Secrets.ExecutionLogName, path, sw.ToString());
    }

    public static FunctionInvokeLogger GetFunctionInvokeLogger()
    {
        var account = Secrets.GetAccount();
        return new FunctionInvokeLogger { _account = account, _tableName = Secrets.FunctionInvokeLogTableName,
                                          _tableMRU = GetFunctionLogTableIndexTime()
        };
    }

    public static FunctionIndexEntity Lookup(FunctionLocation location)
    {
        string rowKey = FunctionIndexEntity.GetRowKey(location);
        return Services.Lookup(rowKey);
    }

    public static FunctionIndexEntity Lookup(string functionId)
    {
        FunctionIndexEntity func = Utility.Lookup<FunctionIndexEntity>(
            Secrets.GetAccount(), 
            Secrets.FunctionIndexTableName, 
            FunctionIndexEntity.PartionKey,
            functionId);
        return func;
    }

    public static void QueueIndexRequest(IndexRequestPayload payload)
    {
        string json = JsonCustom.SerializeObject(payload);
        Secrets.GetOrchestratorControlQueue().AddMessage(new CloudQueueMessage(json));
    }

    // Important to get a quick, estimate of the depth of the execution queu. 
    public static int? GetExecutionQueueDepth()
    {
        var q = GetExecutionQueueSettings().GetQueue();

        return q.RetrieveApproximateMessageCount();
    }

    public static Worker GetOrchestrationWorker()
    {
        var settings = GetOrchestratorSettings();
        return new Orchestrator.Worker(settings);
    }

    public static ExecutionInstanceLogEntity QueueExecutionRequest(FunctionInstance instance)
    {
        instance.Id = Guid.NewGuid(); // used for logging. 
        instance.ServiceUrl = Secrets.WebDashboardUri;

        // Log that the function is now queued.
        // Do this before queueing to avoid racing with execution 
        var logger = GetFunctionInvokeLogger();

        var logItem = new ExecutionInstanceLogEntity();
        logItem.FunctionInstance = instance;
        logItem.QueueTime = DateTime.UtcNow; // don't set starttime until a role actually executes it.

        logger.Log(logItem);        

        ExecutorClient.Queue(GetExecutionQueueSettings(), instance);

        // Now that it's queued, execution node may immediately pick up the queue item and start running it, 
        // and logging against it.
        
        return logItem;                
    }

    public static ExecutionQueueSettings GetExecutionQueueSettings()
    {
        var settings = new ExecutionQueueSettings
        {
            Account = Secrets.GetAccount(),
            QueueName = Secrets.ExecutionQueueName
        };
        return settings;
    }

    public static OrchestratorSettings GetOrchestratorSettings()
    {
        return new OrchestratorSettings();
    }

    public static ExecutionStatsAggregator GetStatsAggregator()
    {
        var table = GetInvokeStatsTable();
        var logger = Services.GetFunctionInvokeLogger(); // for reading logs
        return new ExecutionStatsAggregator(table, logger, GetFunctionLogTableIndexTime());
    }

    public static AzureTable GetInvokeStatsTable()
    {
        return new AzureTables.AzureTable(Secrets.GetAccount(), Secrets.FunctionInvokeStatsTableName);
    }

    public static AzureTable GetFunctionLogTableIndexTime()
    {
        return new AzureTables.AzureTable(Secrets.GetAccount(), Secrets.FunctionInvokeLogIndexTime);
    }

    public static AzureTable<BinderEntry> GetBinderTable()
    {
        return new AzureTable<BinderEntry>(Secrets.GetAccount(), Secrets.BindersTableName);
    }
}


public class ExecutionFinishedPayload
{
    // FunctionInstance Ids for functions that we just finished. 
    // Orchestrator cna look these up and apply the deltas. 
    public Guid[] Instances { get; set; }
}
