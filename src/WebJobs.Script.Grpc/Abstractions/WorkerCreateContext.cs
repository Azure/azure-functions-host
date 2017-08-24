using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public class WorkerCreateContext
    {
        public Uri ServerUri { get; set; }

        public WorkerConfig WorkerConfig { get; set; }

        public string WorkerId { get; set; }

        public string RequestId { get; set; }

        public ILogger Logger { get; set; }

        public string WorkingDirectory { get; set; }
    }
}
