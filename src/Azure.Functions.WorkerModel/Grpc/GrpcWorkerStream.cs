using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Grpc;
using Microsoft.Azure.Functions.WorkerModel.JobHost;
using Microsoft.Azure.Functions.WorkerModel.Workers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
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
    private readonly WorkerModelFunctionMetadataProvider _metadataProvider;
    private Task _readTask;

    private IWorkerState _worker;

    // Keep these separate for better tracking of state
    private WorkerState _workerState;
    private WorkerState _placeholderWorkerState;

    public GrpcWorkerStream(IJobHostManager jobHostManager, IOptions<FunctionApplicationOptions> appOptions, WorkerModelFunctionMetadataProvider metadataProvider)
    {
        _jobHostManager = jobHostManager;
        //_channelRouter = new(_channel);
        _appOptions = appOptions.Value;
        _metadataProvider = metadataProvider;
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
                    case StreamingMessage.ContentOneofCase.WorkerConnect:
                        HandleWorkerConnect(req.WorkerConnect);
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
            (StreamState.None, WorkerAction.WorkerConnect) => StreamState.Initialized,
            (StreamState.Connected, WorkerAction.WorkerInitResponse) => StreamState.Connected,
            (StreamState.Connected, WorkerAction.MetadataResponse) => StreamState.Initialized,
            (StreamState.Connected, WorkerAction.Specialize) when IsPlaceholder => StreamState.Specializing,
            (StreamState.Connected, WorkerAction.InvocationResponse) => StreamState.Running, // This can happen when we don't need a  metadata response
            (StreamState.Initialized, WorkerAction.MetadataResponse) => StreamState.Initialized, // Allow duplicate metadata response from relay
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

    /// <summary>
    /// Handles a WorkerConnect message from the Sidecar.
    /// This replaces the multi-step handshake (StartStream + WorkerInit + FunctionMetadata)
    /// with a single message containing all worker and function info.
    /// </summary>
    private void HandleWorkerConnect(WorkerConnect workerConnect)
    {
        StreamState = ChangeState(WorkerAction.WorkerConnect);

        var runtime = workerConnect.WorkerMetadata?.RuntimeName ?? "unknown";
        var version = workerConnect.WorkerMetadata?.RuntimeVersion ?? "unknown";
        var architecture = workerConnect.WorkerMetadata?.WorkerBitness ?? "unknown";

        var stack = new WorkerStack(runtime, version, architecture, false);
        var capabilities = workerConnect.WorkerCapabilities.ToDictionary();

        string appId = null;
        string appVersion = null;
        workerConnect.WorkerMetadata?.CustomProperties.TryGetValue("ApplicationId", out appId);
        workerConnect.WorkerMetadata?.CustomProperties.TryGetValue("ApplicationVersion", out appVersion);

        appId ??= _appOptions.DefaultApplicationId;
        appVersion ??= _appOptions.DefaultApplicationVersion;

        var id = workerConnect.WorkerId ?? Guid.NewGuid().ToString();
        var appDef = new ApplicationDefinition(appId, appVersion);
        var workerDef = new WorkerDefinition(id, appDef, capabilities, stack);

        _workerState = new WorkerState(workerDef);

        _ = StartJobHostWithMetadataAsync(workerDef, workerConnect);
    }

    /// <summary>
    /// Starts the JobHost and sends FunctionLoadRequests to the worker.
    /// The same metadata objects are used for the JobHost and for FunctionLoadRequests,
    /// ensuring the FunctionId is consistent between load and invocation.
    /// After completion, sends a WorkerConnectResponse so the Sidecar knows the Runtime
    /// is ready to accept HTTP requests (routes are registered, functions are loaded).
    /// </summary>
    private async Task StartJobHostWithMetadataAsync(WorkerDefinition workerDef, WorkerConnect workerConnect)
    {
        try
        {
            // Materialize metadata so the same objects are used by both the JobHost and GrpcWorker
            var metadata = CreateMetadata(workerConnect.FunctionMetadata).ToList();

            await StartNewJobHostAsync(workerDef, _jobHostManager, metadata);

            // Tell GrpcWorker about the functions and send FunctionLoadRequests to the worker.
            // This uses the same FunctionMetadata objects that the JobHost/invoker uses,
            // so GetFunctionId() returns the same value in load requests and invocations.
            if (_worker is GrpcWorker grpcWorker)
            {
                grpcWorker.LoadFunctionsFromMetadata(metadata);
            }

            // Signal the Sidecar that the Runtime is ready: JobHost started, HTTP routes
            // registered, FunctionLoadRequests sent. The Sidecar waits for this before
            // allowing the ScaleController to forward HTTP traffic.
            SendWorkerConnectResponse(StatusResult.Types.Status.Success);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GrpcWorkerStream] StartJobHostWithMetadataAsync failed: {ex.Message}");
            SendWorkerConnectResponse(StatusResult.Types.Status.Failure, ex.Message);
        }
    }

    private void SendWorkerConnectResponse(StatusResult.Types.Status status, string errorMessage = null)
    {
        var response = new WorkerConnectResponse
        {
            Result = new StatusResult { Status = status }
        };

        if (errorMessage is not null)
        {
            response.Result.Exception = new RpcException { Message = errorMessage };
        }

        _channel.WorkerMessageWriter.TryWrite(new MessageToWorker(new StreamingMessage
        {
            WorkerConnectResponse = response
        }));
    }

    private IEnumerable<FunctionMetadata> CreateMetadata(IEnumerable<RpcFunctionMetadata> functionMetadata)
    {
        foreach (var rpcMetadata in functionMetadata)
        {
            yield return ProcessRpcFunctionMetadata(rpcMetadata);
        }
    }

    private FunctionMetadata ProcessRpcFunctionMetadata(RpcFunctionMetadata rpcMetadata)
    {
        try
        {
            var function = new FunctionMetadata
            {
                Name = rpcMetadata.Name,
                ScriptFile = rpcMetadata.ScriptFile,
                EntryPoint = rpcMetadata.EntryPoint,
                Language = rpcMetadata.Language
            };

            Utility.ValidateName(rpcMetadata.Name);

            function.SetFunctionId(rpcMetadata.FunctionId);

            // skip function directory validation because this involves reading function.json

            // skip function ScriptFile validation for now because this involves enumerating file directory

            // populate retry options if json string representation is provided
            //if (!string.IsNullOrEmpty(rpcMetadata.RetryOptions))
            //{
            //    function.Retry = JObject.Parse(rpcMetadata.RetryOptions).ToObject<RetryOptions>();
            //}

            //// retry option validation
            //if (function.Retry is not null)
            //{
            //    Utility.ValidateRetryOptions(function.Retry);
            //}

            // binding validation
            function = ValidateBindings(rpcMetadata.RawBindings, function);

            // add validated metadata to validated list if it gets this far
            //  validatedMetadata.Add(function);

            return function;
        }
        catch (Exception ex)
        {
            // Utility.AddFunctionError(_functionErrors, function.Name, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
        }

        return null;
    }

    internal FunctionMetadata ValidateBindings(IEnumerable<string> rawBindings, FunctionMetadata function)
    {
        HashSet<string> bindingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // This method takes the RawBindings and adds them to the FunctionMetadata object. It's possible
        // to call this twice, and we don't want to duplicate the bindings in that case.
        function.Bindings.Clear();

        foreach (var binding in rawBindings)
        {
            var sanitizedBinding = MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(binding, ScriptConstants.SensitiveMetadataBindingPropertyNames, DateParseHandling.None);
            var functionBinding = BindingMetadata.Create(sanitizedBinding);

            Utility.ValidateBinding(functionBinding);

            // Ensure no duplicate binding names exist
            if (bindingNames.Contains(functionBinding.Name))
            {
                // throw new InvalidOperationException($"{nameof(WorkerFunctionDescriptorProvider)}: Multiple bindings with name '{functionBinding.Name}' discovered. Binding names must be unique.");
            }

            bindingNames.Add(functionBinding.Name);

            // add binding to function.Bindings once validation is complete
            function.Bindings.Add(functionBinding);
        }

        // ensure there is at least one binding after validation
        if (function.Bindings == null || function.Bindings.Count == 0)
        {
            throw new FormatException("At least one binding must be declared.");
        }

        // ensure that there is a trigger binding
        var triggerMetadata = function.InputBindings.FirstOrDefault(p => p.IsTrigger);
        if (triggerMetadata == null)
        {
            throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
        }

        return function;
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

        _ = StartNewJobHostAsync(workerDef, _jobHostManager, null);
    }

    private async Task StartNewJobHostAsync(WorkerDefinition workerDef, IJobHostManager jobHostManager, IEnumerable<FunctionMetadata> metadata)
    {
        _metadataProvider.SetMetadata(metadata);

        var jobHost = await GetOrCreateJobHostAsync(jobHostManager, workerDef.Application, metadata);

        var context = new WorkerCreationContext(workerDef);
        _worker = await jobHost.CreateWorkerAsync(context);

        _ = ReadJobHostStreamAsync(_worker.Channel.WorkerMessageReader);

        await jobHost.StartAsync();
    }

    // TODO: use the JobHostBuilder like in Functions.
    private static Task<JobHost> GetOrCreateJobHostAsync(IJobHostManager jobHostManager, ApplicationDefinition appDef, IEnumerable<FunctionMetadata> metadata)
    {
        return jobHostManager.GetOrAddJobHostAsync(appDef, services =>
        {
            // register our provider that knows how to use the grpc details below
            services.AddSingleton<IWorkerFactory, GrpcWorkerFactory>();
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
        await StartNewJobHostAsync(_workerState.Definition, _jobHostManager, null);
    }
}