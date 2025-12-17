using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace OutOfProcModel.Abstractions.Worker;

internal record MessageFromWorker(StreamingMessage Message);

internal record MessageToWorker(StreamingMessage Message);

