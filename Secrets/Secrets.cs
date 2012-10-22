using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

// This is using code as configuration.
// Hold secrets and configuration 
// Provides common place to list all Azure endpoints.
// This does not describe the schemas, payloads, etc for those endpoints. 
public partial class Secrets
{
    public static string AccountConnectionString 
    {
        get
        {
            return GetAccount().ToString(exportSecrets: true);
        }    
    }

    public static CloudStorageAccount GetAccount()
    {
        return new CloudStorageAccount(new StorageCredentialsAccountAndKey(accountName, accountKey), false);
    }

    // Suffix for quickly switching betten production and private runs.
    // Use only lowercase, no numbers, to comply with all the naming restrictions.
    // (queues are only lower, tables are only alphanumeric)
    //private const string prefix = "daaspriv";
    private const string prefix = "daas";

    // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
    public const string FunctionIndexTableName = "DaasFunctionIndex5";

    public const string FunctionInvokeStatsTableName = "DaasFunctionInvokeStats";

    // Where all function instance logging is written.
    // Table is indexed by FunctionInstance.Guid
    public const string FunctionInvokeLogTableName = prefix + "functionlogs";

    // 2ndary table for FunctionInvokeLogTableName, providing an index by time.
    public const string FunctionInvokeLogIndexTime = "functionlogsIndextime";

    // Queuenames must be all lowercase. 
    public const string ExecutionQueueName = prefix + "-execution";
    
    // This is the container where the role can write console output logs for each run.
    // Useful to ensure this container has public access so that browsers can read the logs
    public const string ExecutionLogName = prefix + "-invoke-log";

    // Container where various roles write critical health status. 
    public const string HealthLogContainerName = prefix + "-health-log";

    public const string OrchestratorControlQueue = prefix + "-orch-control";

    public const string FunctionInvokeDoneQueue = prefix + "-invoke-done";
        
    public const string DaasControlContainerName = prefix + "-control";

    // This blob is used by orchestrator to signal to all the executor nodes to reset.
    // THis is needed when orchestrator makes an update (like upgrading a funcioin) and needs 
    // the executors to clear their caches.
    public static CloudBlob GetExecutorResetControlBlob()
    {
        CloudBlobClient client = Secrets.GetAccount().CreateCloudBlobClient();
        CloudBlobContainer c = client.GetContainerReference(DaasControlContainerName);
        c.CreateIfNotExist();
        CloudBlob b = c.GetBlobReference("executor-reset");
        return b;
    }


    public static CloudQueue GetExecutionCompleteQueue()
    {
        CloudQueueClient client = Secrets.GetAccount().CreateCloudQueueClient();
        var queue = client.GetQueueReference(FunctionInvokeDoneQueue);
        queue.CreateIfNotExist();
        return queue;
    }
        
    public static CloudQueue GetOrchestratorControlQueue()
    {
        CloudQueueClient client = Secrets.GetAccount().CreateCloudQueueClient();
        var queue = client.GetQueueReference(OrchestratorControlQueue);
        queue.CreateIfNotExist();
        return queue;
    }

    public static CloudBlobContainer GetExecutionLogContainer()
    {
        CloudBlobClient client = Secrets.GetAccount().CreateCloudBlobClient();
        CloudBlobContainer c = client.GetContainerReference(ExecutionLogName);
        c.CreateIfNotExist();
        return c;
    }

    public static CloudBlobContainer GetHealthLogContainer()
    {
        CloudBlobClient client = Secrets.GetAccount().CreateCloudBlobClient();
        CloudBlobContainer c = client.GetContainerReference(HealthLogContainerName);
        c.CreateIfNotExist();
        return c;
    }    
}

