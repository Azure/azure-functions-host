using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;

namespace WorkerHarness.Core
{
    internal class WorkerInitializeData : BaseActionData
    {
        internal StreamingMessage? StartStream { get; set; }

        internal StreamingMessage? WorkerInitRequest { get; set; }

        internal StreamingMessage? WorkerInitResponse { get; set; }

    }
}
