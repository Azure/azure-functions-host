using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.TaskClient.Protocol;
using System.Threading;

namespace Common
{
    public class TaskRequestDispatcher
    {
        private TaskCredentialsAccountAndKey credentials;
        private Uri taskTenantUri;
        public int maxWaitAttempts = 100;
        public int sleepPerAttemptSecs = 1;
        public LoggingLevel loggingLevel;

        public TaskRequestDispatcher(String taskTenantUrl, String accountName, String key)
        {
            this.credentials = new TaskCredentialsAccountAndKey(accountName, key);
            this.taskTenantUri = new Uri(taskTenantUrl);
            this.loggingLevel = LoggingLevel.Verbose;
        }

        #region Properties

        public TaskCredentialsAccountAndKey Credentials
        {
            get
            {
                return credentials;
            }
        }

        public Uri TaskTenantUri
        {
            get
            {
                return taskTenantUri;
            }
        }

        #endregion

        #region GET operations

        public WorkItem GetWorkItem(string workitemName)
        {
            GetWorkItemRequest req = new GetWorkItemRequest(taskTenantUri, credentials, workitemName);
            TaskResponse resp = SendRequest(req);
            return ((GetWorkItemResponse)resp).WorkItem;
        }

        public Job GetJob(string workitemName, string jobName)
        {
            GetJobRequest req = new GetJobRequest(taskTenantUri, credentials, workitemName, jobName);
            TaskResponse resp = SendRequest(req);
            return ((GetJobResponse)resp).Job;
        }

        public Task GetTask(string workitemName, string jobName, string taskName)
        {
            GetTaskRequest req = new GetTaskRequest(taskTenantUri, credentials, workitemName, jobName, taskName);
            TaskResponse resp = SendRequest(req);
            return ((GetTaskResponse)resp).Task;
        }

        public GetFileResponse GetTaskFile(string workitemName, string jobName, string taskName, String fileName)
        {
            GetTaskFileRequest req = new GetTaskFileRequest(taskTenantUri, credentials, workitemName, jobName, taskName, fileName);
            TaskResponse resp = SendRequest(req);
            return ((GetFileResponse)resp);
        }

        public IEnumerable<FileItem> ListTaskFiles(string workitemName, string jobName, string taskName)
        {
            return ListTaskFiles(workitemName, jobName, taskName, null, false, null, null);
        }

        public IEnumerable<FileItem> ListTaskFiles(string workitemName, string jobName, string taskName,
            string prefix, bool recursive, int? maxResults, string marker)
        {
            ListTaskFilesRequest req = new ListTaskFilesRequest(taskTenantUri, credentials, workitemName, jobName, taskName);
            req.Prefix = prefix;
            req.Recursive = recursive;
            req.Marker = marker;
            req.MaxResults = maxResults;
            TaskResponse resp = SendRequest(req);
            return ((ListFilesResponse)resp).Files;
        }

        public Pool GetPool(string poolName)
        {
            GetPoolRequest req = new GetPoolRequest(taskTenantUri, credentials, poolName);
            TaskResponse resp = SendRequest(req);
            return ((GetPoolResponse)resp).Pool;
        }

        public TVM GetTVM(string poolName, string tvmName)
        {
            GetTVMRequest req = new GetTVMRequest(taskTenantUri, credentials, poolName, tvmName);
            TaskResponse resp = SendRequest(req);
            return ((GetTVMResponse)resp).TVM;
        }

        public GetFileResponse GetTVMFile(string poolName, String tvmName, String fileName)
        {
            GetTVMFileRequest req = new GetTVMFileRequest(taskTenantUri, credentials, poolName, tvmName, fileName);
            TaskResponse resp = SendRequest(req);
            return ((GetFileResponse)resp);
        }

        public IEnumerable<FileItem> ListTVMFiles(string poolName, string tvmName)
        {
            return ListTVMFiles(poolName, tvmName, null, false, null, null);
        }

        public IEnumerable<FileItem> ListTVMFiles(string poolName, string tvmName,
            string prefix, bool recursive, int? maxResults, string marker)
        {
            ListTVMFilesRequest req = new ListTVMFilesRequest(taskTenantUri, credentials, poolName, tvmName);
            req.Prefix = prefix;
            req.Recursive = recursive;
            req.Marker = marker;
            req.MaxResults = maxResults;
            TaskResponse resp = SendRequest(req);
            return ((ListFilesResponse)resp).Files;
        }

        #endregion

        #region Add operations

        public TaskResponse AddWorkItem(string workitemName, WorkItem workitem)
        {
            AddWorkItemRequest req = new AddWorkItemRequest(taskTenantUri, credentials, workitemName);
            req.WorkItem = workitem;
            return SendRequest(req);
        }

        public TaskResponse AddTask(string workitemName, String jobName, String taskName, Task task)
        {
            AddTaskRequest req = new AddTaskRequest(taskTenantUri, credentials, workitemName, jobName, taskName);
            req.Task = task;
            return SendRequest(req);
        }

        public TaskResponse AddPool(string poolName, Pool pool)
        {
            AddPoolRequest req = new AddPoolRequest(taskTenantUri, credentials, poolName);
            req.Pool = pool;
            return SendRequest(req);
        }

        #endregion

        #region Delete operations

        public TaskResponse DeletePool(string poolName)
        {
            DeletePoolRequest req = new DeletePoolRequest(taskTenantUri, credentials, poolName);
            return SendRequest(req);
        }

        public TaskResponse DeleteWorkitem(string workitemName)
        {
            DeleteWorkItemRequest req = new DeleteWorkItemRequest(taskTenantUri, credentials, workitemName);
            return SendRequest(req);
        }

        #endregion

        #region Wait operations

        public string WaitForJobCreation(string workitemName)
        {
            int numAttempts = 0;

            while (numAttempts < maxWaitAttempts)
            {
                WorkItem wi = GetWorkItem(workitemName);

                if (wi.ExecutionInfo != null &&
                    wi.ExecutionInfo.RecentJob != null)
                {
                    return wi.ExecutionInfo.RecentJob.Name;
                }

                ++numAttempts;

                Thread.Sleep(TimeSpan.FromSeconds(sleepPerAttemptSecs));
            }

            throw new Exception("New job not created");
        }

        public String WaitForNextJobCreation(string workitemName, String prevJobName)
        {
            int numAttempts = 0;

            while (numAttempts < maxWaitAttempts)
            {
                WorkItem wi = GetWorkItem(workitemName);

                if (wi.ExecutionInfo != null &&
                    wi.ExecutionInfo.RecentJob != null &&
                    wi.ExecutionInfo.RecentJob.Name != prevJobName)
                {
                    return wi.ExecutionInfo.RecentJob.Name;
                }

                ++numAttempts;

                Thread.Sleep(TimeSpan.FromSeconds(sleepPerAttemptSecs));
            }

            throw new Exception("New job not created");
        }

        public Task WaitForTaskReachTargetState(string workitemName, String jobName, String taskName, TaskState state)
        {
            int numAttempts = 0;

            while (numAttempts < maxWaitAttempts)
            {
                Task task = GetTask(workitemName, jobName, taskName);

                Console.WriteLine(task.State);
                if (task.State == state)
                {
                    return task;
                }

                ++numAttempts;

                Thread.Sleep(TimeSpan.FromSeconds(sleepPerAttemptSecs));
            }

            throw new Exception("Did not reach desired task state");
        }

        #endregion

        #region Helpers

        public TaskResponse SendRequest(TaskRequest request)
        {
            if (loggingLevel == LoggingLevel.Verbose)
            {
                Console.WriteLine("Submiting request {0}", request.GetType().Name);
            }

            TaskResponse resp = request.Execute();
            LogResponseInfo(resp);
            return resp;
        }

        private void LogResponseInfo(TaskResponse resp)
        {
            if (loggingLevel == LoggingLevel.Verbose)
            {
                Console.WriteLine("RequestId:{0} StatusCode:{1}", resp.RequestId, resp.StatusCode);
            }
        }

        #endregion
    }

    public enum LoggingLevel
    {
        Verbose,
        Minimal,
        None
    }
}
