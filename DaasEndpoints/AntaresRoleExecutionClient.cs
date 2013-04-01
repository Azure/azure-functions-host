using System.Net;
using System.Text;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace DaasEndpoints
{    
    // For using Antares as a worker role. 
    // Queue it like normal, but then do an HTTP ping. 
    public class AntaresRoleExecutionClient : WorkerRoleExecutionClient
    {
        // The url of the antares worker site to be pinged when new work comes in. 
        private readonly string UrlBase;

        public AntaresRoleExecutionClient(string url, CloudQueue queue, IAccountInfo account, IFunctionUpdatedLogger logger, ICausalityLogger causalityLogger)
            : base(queue, account, logger, causalityLogger)
        {
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