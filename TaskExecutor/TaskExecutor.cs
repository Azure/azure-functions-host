using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Common;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.TaskClient.Protocol;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Executor
{
    // Queue a function invocation request via AzureTasks.
    public class TaskExecutor : QueueFunctionBase, IQueueFunction
    {
        // Container name used for communication 
        const string CommContainerName = @"sbat-comm";

        // Container where runner host is saved. This must have been copied up as part of deployment. 
        const string RunnerHostContainerName = @"sbat-runnerhost";

        private readonly string _loggerJson; // json serialization of _logger, passed ot host process.
        private readonly TaskConfig _config;

        // config - configuration information for using AzureTasks. 
        public TaskExecutor(IAccountInfo account, IFunctionUpdatedLogger logger, TaskConfig config)
            : base(account, logger)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            
            // Verify that logger can be serialized, since we'll serialize it into our separate host process. 
            // If it doesn't serialize, let's find out now!
            {
                var settings = JsonCustom.NewSettings();
                settings.TypeNameHandling = TypeNameHandling.Objects; // needed 

                _loggerJson = JsonConvert.SerializeObject(logger, settings);

                logger = JsonCustom.DeserializeObject<IFunctionUpdatedLogger>(_loggerJson);
            }

            _config = config;
        }

        public void CreatePool(int poolSize)
        {
            var poolName = _config.PoolName;
            var taskRequestDispatcher = GetDispatcher();

            Pool pool = new Pool(poolName);
            pool.TargetDedicated = poolSize;
            pool.TVMSize = TVMSize.Small;

            taskRequestDispatcher.AddPool(poolName, pool);
        }

        public void DeletePool()
        {
            var poolName = _config.PoolName;
            var taskRequestDispatcher = GetDispatcher();
            taskRequestDispatcher.DeletePool(poolName);
        }

        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            FunctionInvokeRequest instance = logItem.FunctionInstance;

            var taskRequestDispatcher = GetDispatcher();

            string workitemName = "SimpleBatch" + NewGuid();

            WorkItem wi = new WorkItem(workitemName);
            wi.JobExecutionEnvironment = new JobExecutionEnvironment();
            wi.JobExecutionEnvironment.PoolName = _config.PoolName;

            taskRequestDispatcher.AddWorkItem(workitemName, wi);

            String jobName = taskRequestDispatcher.WaitForJobCreation(workitemName);

            // Target is already uploaded to a blob
            // Create a SAS to that, and add resources.             

            Task task = new Task();

            // "Resources" must be uploaded to a blob (with a SAS) 
            // (This is akin to the local download portion)

            var res = new List<ResourceFile>();
            res.AddRange(GetRunnerHostFiles());

            // Beware! Illegal to have conflicting ResourceFiles. 
            // Naturally avoid this since user code and host code go in separate directories.
            res.AddRange(GetUserFiles(instance.Location));

            // Write out logger file.            
            {
                var inputs = new AzureTaskRunnerHost.ServiceInputs
                {
                     Logger = this._logger,
                     Instance = instance,
                     LocalDir = @".\user",
                     AccountConnectionString = _account.AccountConnectionString,
                     QueueName = "daas-invoke-done" // !!! share with DaasEndpoints
                };

                string json = JsonCustom.SerializeObject(inputs);

                string blobName = string.Format("{0}.logger.txt", instance.Id);
                AddFile("input.logger.txt", blobName, json, res);
            }

            task.Files = res;
            task.CommandLine = string.Format("AzureTaskRunnerHost.exe");
            task.TVMType = TVMType.Dedicated; // what does this mean ???

            string taskName = "Task0";
            taskRequestDispatcher.AddTask(workitemName, jobName, taskName, task);

            // Mark backpointer so that we can retrieve the Azure Task from this.
            logItem.Backpointer = string.Join("|", workitemName, jobName, taskName);
            _logger.Log(logItem); // Persists update to Backpointer 
        }

        // Diangostics helper to block on a task and print its output
        public void WaitAndPrintOutput(ExecutionInstanceLogEntity logItem)
        {
            string ptr = logItem.Backpointer;
            string[] parts = ptr.Split('|');

            string workitemName = parts[0];
            string jobName = parts[1];
            string taskName = parts[2];

            WaitAndPrintOutput(workitemName, jobName, taskName);
        }

        private void WaitAndPrintOutput(string workitemName, string jobName, string taskName)
        {
            TaskRequestDispatcher taskRequestDispatcher = GetDispatcher();
            Task resp = taskRequestDispatcher.WaitForTaskReachTargetState(workitemName, jobName, taskName, TaskState.Completed);

            Console.Write("Task {0} reached completed state. ", taskName);

            TaskUtils.PrintExitCodeOrSchedulingError(resp);
            TaskUtils.ReadStdErrOrOutputBasedOnExitCode(workitemName,
                jobName, resp, taskRequestDispatcher);
        }

        // Upload to our blob, and pass SAS as a ResourceFile to the task. 
        void AddFile(string localFilename, string blobName, string content, List<ResourceFile> res)
        {  
            var accountConnectionString = _account.AccountConnectionString;

            var blob = new CloudBlobDescriptor
            {
                AccountConnectionString = accountConnectionString,
                ContainerName = CommContainerName,
                BlobName = blobName
            };
            var sas = blob.GetBlobSasSig();
            var blob2 = new CloudBlob(sas); // exercise sas

            blob2.UploadText(content);

            res.Add(new ResourceFile
            {
                BlobSource = sas,
                FileName = localFilename, // local filename that the blob gets copied to.
            });
        }


        // Enumerate all files in the container
        private static IEnumerable<ResourceFile> GetFilesInContainer(string accountConnectionString, string containerName, string subDir = null)
        {
            var blob = new CloudBlobDescriptor
            {
                AccountConnectionString = accountConnectionString,
                ContainerName = containerName,
                BlobName = null // ignored
            };
            string sas = blob.GetContainerSasSig();
            var container = blob.GetContainer();

            foreach(var item in container.ListBlobs())
            {
                string fullname = item.Uri.ToString();
                string shortname = System.IO.Path.GetFileName(fullname);
                yield return GetResourceFile(sas, shortname, subDir);
            }
        }

        private IEnumerable<ResourceFile> GetUserFiles(FunctionLocation location)
        {
            var blob = location.Blob;
            return GetFilesInContainer(blob.AccountConnectionString, blob.ContainerName, "user");
        }

        // Common files, same for all tasks. 
        // This can get pulled from a SimpleBatch internal list, not the user's container
        private IEnumerable<ResourceFile> GetRunnerHostFiles()
        {
            var accountConnectionString = _account.AccountConnectionString;
            return GetFilesInContainer(accountConnectionString, RunnerHostContainerName);
        }

        static ResourceFile GetResourceFile(string sasContainer, string filename, string subDir)
        {
            return new ResourceFile
            {
                 FileName = subDir == null ? filename : Path.Combine(subDir, filename),
                 BlobSource = TaskUtils.ConstructBlobSource(sasContainer, filename)
            };
        }

        internal static string NewGuid()
        {
            return Guid.NewGuid().ToString("N");
        }

        private TaskRequestDispatcher GetDispatcher()
        {
            return new TaskRequestDispatcher(_config.TenantUrl, _config.AccountName, _config.Key);
        }
    }


    // Configuration settings used with Azure tasks. Needed for an account.
    public class TaskConfig
    {
        public string TenantUrl { get; set; } // Common, like: https://task.core.windows-int.net 
        public string AccountName { get; set; } 
        public string Key { get; set; } // Secret!

        public string PoolName { get; set; } // assumes already created
    }    
}
