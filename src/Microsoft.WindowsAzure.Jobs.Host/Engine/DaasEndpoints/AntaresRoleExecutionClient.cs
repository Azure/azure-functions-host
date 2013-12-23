using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // For using Antares as a worker role. 
    // Queue it like normal, but then do an HTTP ping. 
    internal class AntaresRoleExecutionClient : WorkerRoleExecutionClient
    {
        // The url of the antares worker site to be pinged when new work comes in. 
        private readonly string _urlBase;

        public AntaresRoleExecutionClient(string url, CloudQueue queue, QueueInterfaces interfaces)
            : base(queue, interfaces)
        {
            if (String.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Antares worker URL is empty", "url");
            }

            _urlBase = url;
        }

        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            base.Work(logItem);
            PingWorker();
        }

        // Send an HTTP request out to a worker. Worker than can dequeue a message.
        private void PingWorker()
        {
            string url = _urlBase + "/api/Worker";
            Web.PostJson(url, new AccountInfo(_account));
        }
    }
}
