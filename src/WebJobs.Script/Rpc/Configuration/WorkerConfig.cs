using Microsoft.Azure.WebJobs.Script.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class WorkerConfig
    {
        public WorkerDescription Description { get; set; }

        public ArgumentsDescription Arguments { get; set; }

        public string Extension => Description.Extension;

        public string Language => Description.Language;
    }
}
