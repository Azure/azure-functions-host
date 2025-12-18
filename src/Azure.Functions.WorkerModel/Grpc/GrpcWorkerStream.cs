using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Grpc;
using Microsoft.Azure.Functions.WorkerModel.JobHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OutOfProcModel.Abstractions.ControlPlane;
using OutOfProcModel.Abstractions.Mock;
using OutOfProcModel.Abstractions.Worker;
using OutOfProcModel.Mock;

namespace OutOfProcModel.FunctionsHost.Grpc;

internal class GrpcWorkerStream
{
    private readonly IJobHostManager _jobHostManager;
    private readonly CancellationTokenSource _stopTokenSource = new();

    // TODO: It's possible for a single worker to connect to us with several streams. They should share
    //       this channel, so it should likely move to a factory where that can be managed.
    private readonly BidirectionalChannel _channel = new();

    //private readonly ChannelRouter _channelRouter;
    private readonly FunctionApplicationOptions _appOptions;
    private Task _readTask;

    private IWorkerState _worker;

    // Keep these separate for better tracking of state
    private WorkerState _workerState;
    private WorkerState _placeholderWorkerState;

    public GrpcWorkerStream(IJobHostManager jobHostManager, IOptions<FunctionApplicationOptions> appOptions)
    {
        _jobHostManager = jobHostManager;
        //_channelRouter = new(_channel);
        _appOptions = appOptions.Value;
    }

    public StreamState StreamState { get; private set; } = StreamState.None;

    private bool IsPlaceholder => GetCurrentWorkerState() == _placeholderWorkerState;

    private WorkerState GetCurrentWorkerState()
    {
        return _workerState ?? _placeholderWorkerState ?? throw new InvalidOperationException("WorkerState is not initialized. Cannot get current worker state.");
    }

    public async IAsyncEnumerable<StreamingMessage> StartAsync(IAsyncEnumerable<StreamingMessage> requests)
    {
        // _channelRouter.Start();

        _readTask = ReadStreamAsync(requests, _stopTokenSource.Token);

        // Return all outgoing messages to the worker
        while (await _channel.WorkerMessageReader.WaitToReadAsync())
        {
            while (_channel.WorkerMessageReader.TryRead(out var message))
            {
                yield return message.Message;
            }
        }
    }

    public async Task StopAsync()
    {
        if (_readTask == null)
        {
            return;
        }

        var workerTerminate = new WorkerTerminate()
        {
            GracePeriod = Duration.FromTimeSpan(TimeSpan.FromSeconds(5))
        };

        var message = new StreamingMessage
        {
            WorkerTerminate = workerTerminate
        };

        if (_channel.WorkerMessageWriter.TryWrite(new MessageToWorker(message)))
        {
            // Signal the worker to shut down gracefully
            var completed = await Task.WhenAny(Task.Delay(5000), _readTask);
            if (completed != _readTask)
            {
                // If the read task didn't complete in time, we cancel it
                _stopTokenSource.Cancel();
            }
        }
        else
        {
            // The worker has already been stopped or the channel is closed
            _stopTokenSource.Cancel();
        }

        await _readTask;
    }

    public async Task ReadJobHostStreamAsync(ChannelReader<MessageToWorker> reader)
    {
        // Allow callers to await this method without blocking the thread
        await Task.Yield();
        try
        {
            while (await reader.WaitToReadAsync(_stopTokenSource.Token))
            {
                while (reader.TryRead(out var msg))
                {
                    _channel.WorkerMessageWriter.TryWrite(msg);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Handle cancellation gracefully
            StreamState = StreamState.Stopped;
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error reading JobHost stream: {ex.Message}");
            StreamState = StreamState.Stopped;
        }
    }

    private async Task ReadStreamAsync(IAsyncEnumerable<StreamingMessage> requests, CancellationToken stopToken)
    {
        // Allow callers to await this method without blocking the thread
        await Task.Yield();

        try
        {
            await foreach (var req in requests.WithCancellation(stopToken))
            {
                // Handle the request
                switch (req.ContentCase)
                {
                    case StreamingMessage.ContentOneofCase.StartStream:
                        HandleStartStream(req.StartStream);
                        break;
                    case StreamingMessage.ContentOneofCase.WorkerInitResponse:
                        HandleWorkerInitResponse(req.WorkerInitResponse);
                        break;
                    case StreamingMessage.ContentOneofCase.FunctionMetadataResponse:
                        StreamState = ChangeState(WorkerAction.MetadataResponse);
                        _worker?.Channel.HostMessageWriter.TryWrite(new MessageFromWorker(req));
                        break;
                    case StreamingMessage.ContentOneofCase.FunctionLoadResponse:
                        _worker?.Channel.HostMessageWriter.TryWrite(new MessageFromWorker(req));
                        break;
                    case StreamingMessage.ContentOneofCase.InvocationResponse:
                        StreamState = ChangeState(WorkerAction.InvocationResponse);
                        _worker?.Channel.HostMessageWriter.TryWrite(new MessageFromWorker(req));
                        break;
                    case StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadResponse:
                        _ = HandleEnvironmentReloadResponseAsync(req.FunctionEnvironmentReloadResponse);
                        break;
                    case StreamingMessage.ContentOneofCase.RpcLog:
                        // nothing for now
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported message type: {req.ContentCase}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Handle cancellation gracefully
            StreamState = StreamState.Stopped;
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error reading stream: {ex.Message}");
            StreamState = StreamState.Stopped;
        }

        // TODO: revisit this. If we get here, it could mean:
        // - stop token was called and we exited b/c of that
        // - the worker disconnected and is no longer sending us data
        // What all do we need to clean up?
        // await StopAsync();

        await _jobHostManager.TryGetJobHostAsync(GetCurrentWorkerState().Definition.Application, out var jobHost);
        if (jobHost != null)
        {
            // await jobHost.WorkerManager.RemoveWorkerAsync(GetCurrentWorkerState().Definition.WorkerId);
        }
    }

    private void HandleJobHostMessage(StreamingMessage req)
    {
        var msgType = MapMessageType(req.ContentCase);
        ThrowIfInvalidState(msgType);

        // translate from grpc and send to JobHost
        var msgFromWorker = new MessageFromWorker(req);
        _ = _jobHostManager.HandleMessageAsync(msgFromWorker);

        StreamState = ChangeState(MapMessageType(req.ContentCase));

        static WorkerAction MapMessageType(StreamingMessage.ContentOneofCase messageType) =>
            messageType switch
            {
                StreamingMessage.ContentOneofCase.FunctionMetadataResponse => WorkerAction.MetadataResponse, // the only one for now
                _ => throw new InvalidOperationException($"Unknown message type: {messageType}")
            };
    }

    private void ThrowIfInvalidState(WorkerAction action)
    {
        _ = ChangeState(action);
    }

    private StreamState ChangeState(WorkerAction action) =>
        (StreamState, action) switch
        {
            (StreamState.None, WorkerAction.StartStream) => StreamState.Connected,
            (StreamState.Connected, WorkerAction.WorkerInitResponse) => StreamState.Connected,
            (StreamState.Connected, WorkerAction.MetadataResponse) => StreamState.Initialized,
            (StreamState.Connected, WorkerAction.Specialize) when IsPlaceholder => StreamState.Specializing,
            (StreamState.Connected, WorkerAction.InvocationResponse) => StreamState.Running, // This can happen when we don't need a  metadata response
            (StreamState.Initialized, WorkerAction.InvocationResponse) when IsPlaceholder => StreamState.RunningAsPlaceholder,
            (StreamState.Initialized, WorkerAction.InvocationResponse) when !IsPlaceholder => StreamState.Running,
            (StreamState.RunningAsPlaceholder, WorkerAction.Specialize) => StreamState.Specializing,
            (StreamState.RunningAsPlaceholder, WorkerAction.InvocationResponse) => StreamState.RunningAsPlaceholder,
            (StreamState.Specializing, WorkerAction.EnvironmentReloadResponse) => StreamState.Connected,
            (StreamState.Running, WorkerAction.InvocationResponse) => StreamState.Running,
            (StreamState.Running, WorkerAction.Specialize) => StreamState.Specializing,
            _ => throw new InvalidOperationException($"Cannot change state from '{StreamState}' with '{action}'.")
        };

    private void HandleStartStream(StartStream startStream)
    {
        StreamState = ChangeState(WorkerAction.StartStream);

        _channel.WorkerMessageWriter.TryWrite(new MessageToWorker(new StreamingMessage
        {
            WorkerInitRequest = new WorkerInitRequest
            {
                HostVersion = ScriptHost.Version,
                WorkerDirectory = string.Empty, // _workerConfig.Description.WorkerDirectory
                FunctionAppDirectory = _appOptions.ApplicationRoot //_applicationHostOptions.CurrentValue.ScriptPath
                // TODO: Build capabilities
            }
        }));
    }

    private void HandleWorkerInitResponse(WorkerInitResponse workerInitResponse)
    {
        // TODO: Handle failure result

        var runtime = workerInitResponse.WorkerMetadata.RuntimeName;
        var version = workerInitResponse.WorkerMetadata.RuntimeVersion;
        var architecture = workerInitResponse.WorkerMetadata.WorkerBitness;

        var isPlaceholder = false; // bool.TryParse(startStream.Properties[nameof(WorkerStack.IsPlaceholder)], out var placeholder) && placeholder;

        var stack = new WorkerStack(runtime, version, architecture, isPlaceholder);
        var capabilities = workerInitResponse.Capabilities.ToDictionary();

        workerInitResponse.WorkerMetadata.CustomProperties.TryGetValue("ApplicationId", out string appId);
        workerInitResponse.WorkerMetadata.CustomProperties.TryGetValue("ApplicationVersion", out string appVersion);

        appId ??= _appOptions.DefaultApplicationId;
        appVersion ??= _appOptions.DefaultApplicationVersion;

        // TODO: Get id from StartStream?
        var id = Guid.NewGuid().ToString();
        var appDef = new ApplicationDefinition(appId, appVersion);
        var workerDef = new WorkerDefinition(id, appDef, capabilities, stack);

        if (workerDef.Stack.IsPlaceholder)
        {
            _placeholderWorkerState = new WorkerState(workerDef);
        }
        else
        {
            _workerState = new WorkerState(workerDef);
        }

        StreamState = ChangeState(WorkerAction.WorkerInitResponse);

        _ = StartNewJobHostAsync(workerDef, _jobHostManager);
    }

    private async Task StartNewJobHostAsync(WorkerDefinition workerDef, IJobHostManager jobHostManager)
    {
        var jobHost = await GetOrCreateJobHostAsync(workerDef, jobHostManager);

        var context = new WorkerCreationContext(workerDef);
        _worker = await jobHost.CreateWorkerAsync(context);

        _ = ReadJobHostStreamAsync(_worker.Channel.WorkerMessageReader);

        await jobHost.StartAsync();
    }

    // TODO: use the JobHostBuilder like in Functions.
    private static Task<JobHost> GetOrCreateJobHostAsync(WorkerDefinition workerDef, IJobHostManager jobHostManager)
    {
        return jobHostManager.GetOrAddJobHostAsync(workerDef.Application, services =>
        {
            // register our provider that knows how to use the grpc details below
            // services.AddSingleton<IWorkerChannelWriterProvider>(channelWriterProvider);
            // services.AddSingleton(p => new GrpcFunctionMetadataFactory(workerDef.Application.ApplicationId, p.GetRequiredService<IWorkerChannelWriterProvider>()));
            // services.AddSingleton<IFunctionMetadataFactory>(p => p.GetRequiredService<GrpcFunctionMetadataFactory>());
            // services.AddSingleton<IMessageHandler>(p => p.GetRequiredService<GrpcFunctionMetadataFactory>());
            services.AddSingleton<IWorkerFactory, GrpcWorkerFactory>();

            if (workerDef.Stack.IsPlaceholder)
            {
                // TODO: Implement placeholder resolver
                // services.AddSingleton<IWorkerResolver, PlaceholderWorkerResolver>();
            }
        });
    }

    // Note this is prototype - would likely come from an OptionsMonitor or some token similar to how it does today.
    public async Task<bool> TrySpecializeAsync(string applicationId, string applicationVersion, WorkerStack runtimeEnvironmentToMatch)
    {
        if (_placeholderWorkerState is null || !IsPlaceholder)
        {
            return false;
        }

        if (_placeholderWorkerState.Definition.Stack != runtimeEnvironmentToMatch with { IsPlaceholder = true })
        {
            return false;
        }

        StreamState = ChangeState(WorkerAction.Specialize);

        // This will drain/stop this specific IWorker and preserve the Channels for specialization. Others will be shutdown outside of this class.
        // TODO -- is there a way we can guarantee this? Like a chained Channel that we can disconnect and Close()?
        var currentDef = GetCurrentWorkerState().Definition;
        await _jobHostManager.RemoveJobHostAsync(currentDef.Application);

        // Tell the worker to specialize with new environment details.
        // It will respond back to us with its details (like capabilities).
        _channel.WorkerMessageWriter.TryWrite(new MessageToWorker(new StreamingMessage
        {
            FunctionEnvironmentReloadRequest = new FunctionEnvironmentReloadRequest()
            // TODO: Fill in
            //new Dictionary<string, string>
            //{
            //    { "ApplicationId", applicationId },
            //    { "ApplicationVersion", applicationVersion },
            //    // TODO: Env vars, etc
            //}));
        }));

        return true;
        // Continues in HandleEnvironmentReloadResponseAsync()
    }

    private async Task HandleEnvironmentReloadResponseAsync(FunctionEnvironmentReloadResponse rpc)
    {
        if (_placeholderWorkerState is null || _workerState is not null)
        {
            throw new InvalidOperationException("WorkerState is not initialized as expected. Cannot handle environment reload response.");
        }

        StreamState = ChangeState(WorkerAction.EnvironmentReloadResponse);

        _workerState = null; //TODO: _placeholderWorkerState.Specialize(rpc.Properties["ApplicationId"], rpc.Properties["ApplicationVersion"], capabilities);
        await StartNewJobHostAsync(_workerState.Definition, _jobHostManager);
    }
}