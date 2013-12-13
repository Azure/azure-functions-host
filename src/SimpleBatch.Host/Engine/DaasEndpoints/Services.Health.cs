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
using System.Diagnostics;

namespace DaasEndpoints
{
    internal partial class Services
    {
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

        public void WriteHealthStatus(OrchestratorRoleHeartbeat status)
        {
            string content = JsonCustom.SerializeObject(status);
            GetHealthLogContainer().GetBlobReference(@"orch\role.txt").UploadText(content);
        }

        public void WriteHealthStatus(string role, ExecutionRoleHeartbeat status)
        {
            string content = JsonCustom.SerializeObject(status);
            GetHealthLogContainer().GetBlobReference(@"exec\" + role + ".txt").UploadText(content);
        }

        // Delete all the blobs in the hleath status container. This will clear out stale entries.
        // active nodes will refresh. 
        public void ResetHealthStatus()
        {
            try
            {
                BlobRequestOptions opts = new BlobRequestOptions { UseFlatBlobListing = true };
                foreach (CloudBlob blob in GetHealthLogContainer().ListBlobs(opts))
                {
                    blob.DeleteIfExists();
                }
            }
            catch
            {
            }
        }
    }


    internal class ServiceHealthStatus
    {
        public IDictionary<string, ExecutionRoleHeartbeat> Executors { get; set; }

        public OrchestratorRoleHeartbeat Orchestrator { get; set; }
    }
}
