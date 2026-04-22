// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Sockets;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

const string WorkerId = "w_mock0001";
const string FunctionName = "HttpTrigger";
string functionId = Guid.NewGuid().ToString();

// Pick a free TCP port the same way the .NET isolated worker SDK's HttpUriProvider
// does (see Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore.Utilities):
// bind a socket to loopback:0, read the kernel-assigned port, then release it before
// the real listener binds. This mirrors how a production worker advertises HttpUri,
// so the worker proxy exercises the same dynamic-port discovery path it will hit
// in deployed scenarios. There is intentionally no override.
int httpPort = GetUnusedTcpPort();

// Listen on loopback only (localhost) — same as the isolated worker SDK in production.
// Cross-container scenarios are not supported by this mock; use SampleIsolatedApp for those.
var httpListener = new HttpListener();
httpListener.Prefixes.Add($"http://localhost:{httpPort}/");
httpListener.Start();
Console.WriteLine($"[MockWorker] HTTP server listening on http://localhost:{httpPort}/");

// Handle HTTP requests on a background task.
_ = Task.Run(async () =>
{
    while (httpListener.IsListening)
    {
        try
        {
            var ctx = await httpListener.GetContextAsync();
            Console.WriteLine($"[MockWorker] HTTP {ctx.Request.HttpMethod} {ctx.Request.Url}");
            byte[] body = System.Text.Encoding.UTF8.GetBytes("Hello from mock worker!");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        }
        catch (ObjectDisposedException)
        {
            break;
        }
    }
});

string grpcEndpoint = ResolveGrpcEndpoint(args);
Console.WriteLine($"[MockWorker] Connecting to worker proxy at {grpcEndpoint}");

using GrpcChannel channel = GrpcChannel.ForAddress(grpcEndpoint);
var client = new FunctionRpc.FunctionRpcClient(channel);
using AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> call = client.EventStream();

// Send StartStream to identify this worker.
await call.RequestStream.WriteAsync(new StreamingMessage
{
    StartStream = new StartStream { WorkerId = WorkerId }
});
Console.WriteLine($"[MockWorker] Sent StartStream (WorkerId={WorkerId})");

// Read messages from the host / sidecar and respond.
while (await call.ResponseStream.MoveNext(CancellationToken.None))
{
    StreamingMessage msg = call.ResponseStream.Current;
    Console.WriteLine($"[MockWorker] ← Received {msg.ContentCase} (RequestId={msg.RequestId})");

    switch (msg.ContentCase)
    {
        case StreamingMessage.ContentOneofCase.WorkerInitRequest:
            await HandleWorkerInit(call.RequestStream, msg);
            break;

        case StreamingMessage.ContentOneofCase.FunctionsMetadataRequest:
            await HandleFunctionMetadata(call.RequestStream, msg);
            break;

        case StreamingMessage.ContentOneofCase.FunctionLoadRequest:
            await HandleFunctionLoad(call.RequestStream, msg);
            break;

        case StreamingMessage.ContentOneofCase.InvocationRequest:
            await HandleInvocation(call.RequestStream, msg);
            break;

        case StreamingMessage.ContentOneofCase.WorkerStatusRequest:
            await HandleWorkerStatus(call.RequestStream, msg);
            break;

        case StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest:
            await HandleEnvironmentReload(call.RequestStream, msg);
            break;

        default:
            Console.WriteLine($"[MockWorker]   (ignored)");
            break;
    }
}

Console.WriteLine("[MockWorker] Stream closed. Exiting.");
httpListener.Stop();

// ─── Handlers ───────────────────────────────────────────────────────────────────

async Task HandleWorkerInit(IClientStreamWriter<StreamingMessage> stream, StreamingMessage msg)
{
    var response = new StreamingMessage
    {
        RequestId = msg.RequestId,
        WorkerInitResponse = new WorkerInitResponse
        {
            Result = new StatusResult { Status = StatusResult.Types.Status.Success },
            WorkerMetadata = new WorkerMetadata
            {
                RuntimeName = "mock",
                RuntimeVersion = "1.0.0",
                WorkerVersion = "1.0.0",
                WorkerBitness = "x64"
            },
            Capabilities =
            {
                ["host_configuration_json"] = "{\"version\":\"2.0\"}",
                ["RpcHttpTriggerMetadataRemoved"] = "true",
                ["RawHttpBodyBytes"] = "true",
                ["HttpUri"] = $"http://localhost:{httpPort}"
            }
        }
    };

    await stream.WriteAsync(response);
    Console.WriteLine("[MockWorker] → Sent WorkerInitResponse (Success)");
}

async Task HandleEnvironmentReload(IClientStreamWriter<StreamingMessage> stream, StreamingMessage msg)
{
    var dir = msg.FunctionEnvironmentReloadRequest?.FunctionAppDirectory ?? "(none)";
    Console.WriteLine($"[MockWorker]   FunctionAppDirectory={dir}");

    var response = new StreamingMessage
    {
        RequestId = msg.RequestId,
        FunctionEnvironmentReloadResponse = new FunctionEnvironmentReloadResponse
        {
            Result = new StatusResult { Status = StatusResult.Types.Status.Success }
        }
    };

    await stream.WriteAsync(response);
    Console.WriteLine("[MockWorker] → Sent FunctionEnvironmentReloadResponse (Success)");
}

async Task HandleFunctionMetadata(IClientStreamWriter<StreamingMessage> stream, StreamingMessage msg)
{
    var metadata = new RpcFunctionMetadata
    {
        Name = FunctionName,
        FunctionId = functionId,
        Language = "mock",
        ScriptFile = "worker.mock",
        Bindings =
        {
            ["req"] = new BindingInfo
            {
                Type = "httpTrigger",
                Direction = BindingInfo.Types.Direction.In
            },
            ["$return"] = new BindingInfo
            {
                Type = "http",
                Direction = BindingInfo.Types.Direction.Out
            }
        },
        RawBindings =
        {
            "{\"name\":\"req\",\"type\":\"httpTrigger\",\"direction\":\"in\",\"authLevel\":\"anonymous\"}",
            "{\"name\":\"$return\",\"type\":\"http\",\"direction\":\"out\"}"
        }
    };

    var response = new StreamingMessage
    {
        RequestId = msg.RequestId,
        FunctionMetadataResponse = new FunctionMetadataResponse
        {
            Result = new StatusResult { Status = StatusResult.Types.Status.Success },
            UseDefaultMetadataIndexing = false,
            FunctionMetadataResults = { metadata }
        }
    };

    await stream.WriteAsync(response);
    Console.WriteLine($"[MockWorker] → Sent FunctionMetadataResponse (1 function: {FunctionName})");
}

async Task HandleFunctionLoad(IClientStreamWriter<StreamingMessage> stream, StreamingMessage msg)
{
    var response = new StreamingMessage
    {
        RequestId = msg.RequestId,
        FunctionLoadResponse = new FunctionLoadResponse
        {
            FunctionId = msg.FunctionLoadRequest.FunctionId,
            Result = new StatusResult { Status = StatusResult.Types.Status.Success }
        }
    };

    await stream.WriteAsync(response);
    Console.WriteLine($"[MockWorker] → Sent FunctionLoadResponse (FunctionId={msg.FunctionLoadRequest.FunctionId}, Success)");
}

async Task HandleInvocation(IClientStreamWriter<StreamingMessage> stream, StreamingMessage msg)
{
    var httpResponse = new RpcHttp
    {
        StatusCode = "200",
        Body = new TypedData { String = "Hello from mock worker! (grpc)" }
    };

    var response = new StreamingMessage
    {
        RequestId = msg.RequestId,
        InvocationResponse = new InvocationResponse
        {
            InvocationId = msg.InvocationRequest.InvocationId,
            Result = new StatusResult { Status = StatusResult.Types.Status.Success },
            ReturnValue = new TypedData { Http = httpResponse }
        }
    };

    await stream.WriteAsync(response);
    Console.WriteLine($"[MockWorker] → Sent InvocationResponse (InvocationId={msg.InvocationRequest.InvocationId}, 200 OK)");
}

async Task HandleWorkerStatus(IClientStreamWriter<StreamingMessage> stream, StreamingMessage msg)
{
    var response = new StreamingMessage
    {
        RequestId = msg.RequestId,
        WorkerStatusResponse = new WorkerStatusResponse()
    };

    await stream.WriteAsync(response);
    Console.WriteLine("[MockWorker] → Sent WorkerStatusResponse");
}

// ─── Helpers ────────────────────────────────────────────────────────────────────

static string ResolveGrpcEndpoint(string[] args)
{
    // 1. --grpc-endpoint <url> from command-line args
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--grpc-endpoint", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    // 2. FUNCTIONS_GRPC_HOST / FUNCTIONS_GRPC_PORT env vars
    string? host = Environment.GetEnvironmentVariable("FUNCTIONS_GRPC_HOST");
    string? port = Environment.GetEnvironmentVariable("FUNCTIONS_GRPC_PORT");

    if (!string.IsNullOrEmpty(host) || !string.IsNullOrEmpty(port))
    {
        return $"http://{host ?? "localhost"}:{port ?? "50052"}";
    }

    // 3. Default
    return "http://localhost:50052";
}

// Mirrors Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore.Utilities.GetUnusedTcpPort
// from the .NET isolated worker SDK: bind a TCP socket to loopback with port 0, let the
// kernel assign a free port, capture it via LocalEndPoint, then dispose the socket so the
// real HttpListener can bind to that port. There is a brief TOCTOU window between dispose
// and rebind, identical to the production code path.
static int GetUnusedTcpPort()
{
    using Socket tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    tcpSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    return ((IPEndPoint)tcpSocket.LocalEndPoint!).Port;
}
