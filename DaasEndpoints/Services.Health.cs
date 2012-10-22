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

public partial class Services
{
    public static ServiceHealthStatus GetHealthStatus()
    {
        var stats = new ServiceHealthStatus();

        stats.Executors = new Dictionary<string, ExecutionRoleHeartbeat>();

        BlobRequestOptions opts = new BlobRequestOptions { UseFlatBlobListing = true };
        foreach (CloudBlob blob in Secrets.GetHealthLogContainer().ListBlobs(opts))
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
            catch (JsonSerializationException)
            {
                // Ignore serialization errors. This is just health status. 
            }
        }

        return stats;
    }

    public static void WriteHealthStatus(OrchestratorRoleHeartbeat status)
    {
        string content = JsonCustom.SerializeObject(status);
        Secrets.GetHealthLogContainer().GetBlobReference(@"orch\role.txt").UploadText(content);
    }

    public static void WriteHealthStatus(string role, ExecutionRoleHeartbeat status)
    {
        string content = JsonCustom.SerializeObject(status);
        Secrets.GetHealthLogContainer().GetBlobReference(@"exec\" + role + ".txt").UploadText(content);
    }

    // Delete all the blobs in the hleath status container. This will clear out stale entries.
    // active nodes will refresh. 
    public static void ResetHealthStatus()
    {
        try
        {
            BlobRequestOptions opts = new BlobRequestOptions { UseFlatBlobListing = true };
            foreach (CloudBlob blob in Secrets.GetHealthLogContainer().ListBlobs(opts))
            {
                blob.DeleteIfExists();
            }
        }
        catch
        {
        }
    }
}

public class ServiceHealthStatus
{
    public IDictionary<string, ExecutionRoleHeartbeat> Executors { get; set; }

    public OrchestratorRoleHeartbeat Orchestrator { get; set; }
}

