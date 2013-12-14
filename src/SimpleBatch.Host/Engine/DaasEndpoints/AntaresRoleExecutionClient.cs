using System;

using Microsoft.WindowsAzure.StorageClient;


namespace Microsoft.WindowsAzure.Jobs
{    
    // For using Antares as a worker role. 
    // Queue it like normal, but then do an HTTP ping. 
    internal class AntaresRoleExecutionClient : WorkerRoleExecutionClient
    {
        // The url of the antares worker site to be pinged when new work comes in. 
        private readonly string UrlBase;

        public AntaresRoleExecutionClient(string url, CloudQueue queue, QueueInterfaces interfaces)
            : base(queue, interfaces)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("Antares worker url is empty");
            }

            this.UrlBase = url;
        }

        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            base.Work(logItem);
            PingWorker();
        }

        // Send an HTTP request out to a worker. Worker than can dequeue a message.
        void PingWorker()
        {
            string url = UrlBase + "/api/Worker";
            Utility.PostJson(url, new AccountInfo(this._account));
        }
    }
}