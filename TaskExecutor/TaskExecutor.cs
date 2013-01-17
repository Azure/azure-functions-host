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
    public class TaskExecutor : IQueueFunction
    {
        // Container name used for communication 
        const string CommContainerName = @"sbat-comm";

        // Container where runner host is saved. This must have been copied up as part of deployment. 
        const string RunnerHostContainerName = @"sbat-runnerhost";



        private readonly IFunctionUpdatedLogger _logger;
        private readonly string _loggerJson; // json serialization of _logger, passed ot host process.
        private readonly IAccountInfo _account;
        private readonly TaskConfig _config;

        // account - this is the internal storage account for using the service. 
        // logger - used for updating the status of the function that gets queued. This must be serializable with JSon since
        //          it will get passed to the host process in an azure task.
        // config - configuration information for using AzureTasks. 
        public TaskExecutor(IAccountInfo account, IFunctionUpdatedLogger logger, TaskConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }
            if (account == null)
            {
                throw new ArgumentNullException("account");
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
            _logger = logger;
            _account = account;
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


        public ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance)
        {
            instance.Id = Guid.NewGuid(); // used for logging. 
            instance.ServiceUrl = _account.WebDashboardUri;
            // Log that the function is now queued.
            // Do this before queueing to avoid racing with execution 
            var logItem = new ExecutionInstanceLogEntity();
            logItem.FunctionInstance = instance;
            logItem.QueueTime = DateTime.UtcNow; // don't set starttime until a role actually executes it.

            _logger.Log(logItem);

            Work(instance);

            _logger.Log(logItem);
            return logItem;
        }

        void Work(FunctionInvokeRequest instance)
        {
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
                     AccountConnectionString = _account.AccountConnectionString, // !!! share with DaasEndpoints
                     QueueName = "daas-invoke-done"
                };

                string json = JsonCustom.SerializeObject(inputs);

                string blobName = string.Format("{0}.logger.txt", instance.Id);
                AddFile("input.logger.txt", blobName, json, res);
            }

            task.Files = res;
            task.CommandLine = string.Format("AzureTaskRunnerHost.exe 15");
            task.TVMType = TVMType.Dedicated; // !!! what does this mean ???

            taskRequestDispatcher.AddTask(workitemName, jobName, "task1", task);

            // Assume pool is already created
            // Create a WorkItem / Job / Task
            // Task:
            // - command line
            // - resources 

            // !!! Debugging code to wait for the task. 
            {
                Task resp = taskRequestDispatcher.WaitForTaskReachTargetState(workitemName, jobName, "Task1", TaskState.Completed);

                Console.Write("Task {0} reached completed state. ", "Task0");

                TaskUtils.PrintExitCodeOrSchedulingError(resp);
                TaskUtils.ReadStdErrOrOutputBasedOnExitCode(workitemName,
                    jobName, resp, taskRequestDispatcher);
            }
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
