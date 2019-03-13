// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;
using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly TimeSpan processStartTimeout = TimeSpan.FromSeconds(40);
        private readonly TimeSpan workerInitTimeout = TimeSpan.FromSeconds(30);
        private readonly string _rootScriptPath;
        private readonly IScriptEventManager _eventManager;
        private readonly WorkerConfig _workerConfig;
        private readonly ILogger _workerChannelLogger;
        private readonly ILanguageWorkerConsoleLogSource _consoleLogSource;
        private readonly IWorkerProcessFactory _processFactory;
        private readonly IProcessRegistry _processRegistry;

        private bool _disposed;
        private bool _isWebHostChannel;
        private IObservable<FunctionRegistrationContext> _functionRegistrations;
        private WorkerInitResponse _initMessage;
        private string _workerId;
        private ILanguageWorkerProcess _languageWorkerProcess;
        private IDictionary<string, BufferBlock<ScriptInvocationContext>> _functionInputBuffers = new Dictionary<string, BufferBlock<ScriptInvocationContext>>();
        private IDictionary<string, Exception> _functionLoadErrors = new Dictionary<string, Exception>();
        private ConcurrentDictionary<string, ScriptInvocationContext> _executingInvocations = new ConcurrentDictionary<string, ScriptInvocationContext>();
        private IObservable<InboundEvent> _inboundWorkerEvents;
        private List<IDisposable> _inputLinks = new List<IDisposable>();
        private List<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private IDisposable _startSubscription;
        private IDisposable _startLatencyMetric;
        private Uri _serverUri;

        internal LanguageWorkerChannel()
        {
            // To help with unit tests
        }

        internal LanguageWorkerChannel(
           string workerId,
           string rootScriptPath,
           IScriptEventManager eventManager,
           IObservable<FunctionRegistrationContext> functionRegistrations,
           IWorkerProcessFactory processFactory,
           IProcessRegistry processRegistry,
           WorkerConfig workerConfig,
           Uri serverUri,
           ILoggerFactory loggerFactory,
           IMetricsLogger metricsLogger,
           int attemptCount,
           ILanguageWorkerConsoleLogSource consoleLogSource,
           bool isWebHostChannel = false)
        {
            _workerId = workerId;
            _functionRegistrations = functionRegistrations;
            _rootScriptPath = rootScriptPath;
            _eventManager = eventManager;
            _workerConfig = workerConfig;
            _serverUri = serverUri;
            _isWebHostChannel = isWebHostChannel;
            _workerChannelLogger = loggerFactory.CreateLogger($"Worker.{workerConfig.Language}.{_workerId}");
            _consoleLogSource = consoleLogSource;
            _processRegistry = processRegistry;
            _processFactory = processFactory;

            _inboundWorkerEvents = _eventManager.OfType<InboundEvent>()
                .Where(msg => msg.WorkerId == _workerId);

            _eventSubscriptions.Add(_inboundWorkerEvents
                .Where(msg => msg.MessageType == MsgType.RpcLog)
                .Subscribe(Log));

            _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                .Where(msg => Config.Extensions.Contains(Path.GetExtension(msg.FileChangeArguments.FullPath)))
                .Throttle(TimeSpan.FromMilliseconds(300)) // debounce
                .Subscribe(msg => _eventManager.Publish(new HostRestartEvent())));

            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.FunctionLoadResponse)
                .Subscribe((msg) => LoadResponse(msg.Message.FunctionLoadResponse)));

            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.InvocationResponse)
                .Subscribe((msg) => InvokeResponse(msg.Message.InvocationResponse)));

            _startLatencyMetric = metricsLogger?.LatencyEvent(string.Format(MetricEventNames.WorkerInitializeLatency, workerConfig.Language, attemptCount));
        }

        public string Id => _workerId;

        public WorkerConfig Config => _workerConfig;

        internal Process WorkerProcess => _languageWorkerProcess.WorkerProcess;

        public bool IsWebhostChannel => _isWebHostChannel;

        public void StartWorkerProcess()
        {
            _startSubscription = _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.StartStream)
                .Timeout(processStartTimeout)
                .Take(1)
                .Subscribe(SendWorkerInitRequest, HandleWorkerError);

            var workerContext = new WorkerContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                MaxMessageLength = LanguageWorkerConstants.DefaultMaxMessageLengthBytes,
                WorkerId = _workerId,
                Arguments = _workerConfig.Arguments,
                WorkingDirectory = _rootScriptPath,
                ServerUri = _serverUri,
            };

            _languageWorkerProcess = new LanguageWorkerProcess(_workerConfig.Language, _workerId, workerContext, _eventManager, _processFactory, _processRegistry, _workerChannelLogger, _consoleLogSource);
            _languageWorkerProcess.StartProcess();
        }

        public void ShutdownWorkerProcess()
        {
            _languageWorkerProcess?.ShutdownProcess();
        }

        // send capabilities to worker, wait for WorkerInitResponse
        internal void SendWorkerInitRequest(RpcEvent startEvent)
        {
            _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.WorkerInitResponse)
                .Timeout(workerInitTimeout)
                .Take(1)
                .Subscribe(PublishWebhostRpcChannelReadyEvent, HandleWorkerError);

            SendStreamingMessage(new StreamingMessage
            {
                WorkerInitRequest = new WorkerInitRequest()
                {
                    HostVersion = ScriptHost.Version
                }
            });
        }

        internal void PublishWorkerProcessReadyEvent(FunctionEnvironmentReloadResponse res)
        {
            WorkerProcessReadyEvent wpEvent = new WorkerProcessReadyEvent(_workerId, _workerConfig.Language);
            _eventManager.Publish(wpEvent);
        }

        internal void PublishWebhostRpcChannelReadyEvent(RpcEvent initEvent)
        {
            _startLatencyMetric?.Dispose();
            _startLatencyMetric = null;

            _initMessage = initEvent.Message.WorkerInitResponse;
            if (_initMessage.Result.IsFailure(out Exception exc))
            {
                HandleWorkerError(exc);
                return;
            }
            if (_functionRegistrations == null)
            {
                RpcWebHostChannelReadyEvent readyEvent = new RpcWebHostChannelReadyEvent(_workerId, _workerConfig.Language, this, _initMessage.WorkerVersion, _initMessage.Capabilities);
                _eventManager.Publish(readyEvent);
            }
            else
            {
                RegisterFunctions(_functionRegistrations);
            }
        }

        public void RegisterFunctions(IObservable<FunctionRegistrationContext> functionRegistrations)
        {
            _functionRegistrations = functionRegistrations ?? throw new ArgumentNullException(nameof(functionRegistrations));
            _eventSubscriptions.Add(_functionRegistrations.Subscribe(SendFunctionLoadRequest));
        }

        public void SendFunctionEnvironmentReloadRequest()
        {
            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.FunctionEnvironmentReloadResponse)
      .Subscribe((msg) => PublishWorkerProcessReadyEvent(msg.Message.FunctionEnvironmentReloadResponse)));

            IDictionary processEnv = Environment.GetEnvironmentVariables();

            FunctionEnvironmentReloadRequest request = new FunctionEnvironmentReloadRequest();
            foreach (DictionaryEntry entry in processEnv)
            {
                request.EnvironmentVariables.Add(entry.Key.ToString(), entry.Value.ToString());
            }

            SendStreamingMessage(new StreamingMessage
            {
                FunctionEnvironmentReloadRequest = request
            });
        }

        internal void SendFunctionLoadRequest(FunctionRegistrationContext context)
        {
            FunctionMetadata metadata = context.Metadata;

            // associate the invocation input buffer with the function
            _functionInputBuffers[context.Metadata.FunctionId] = context.InputBuffer;

            // send a load request for the registered function
            FunctionLoadRequest request = new FunctionLoadRequest()
            {
                FunctionId = metadata.FunctionId,
                Metadata = new RpcFunctionMetadata()
                {
                    Name = metadata.Name,
                    Directory = metadata.FunctionDirectory ?? string.Empty,
                    EntryPoint = metadata.EntryPoint ?? string.Empty,
                    ScriptFile = metadata.ScriptFile ?? string.Empty
                }
            };

            foreach (var binding in metadata.Bindings)
            {
                BindingInfo bindingInfo = binding.ToBindingInfo();

                request.Metadata.Bindings.Add(binding.Name, bindingInfo);
            }

            SendStreamingMessage(new StreamingMessage
            {
                FunctionLoadRequest = request
            });
        }

        internal void LoadResponse(FunctionLoadResponse loadResponse)
        {
            if (loadResponse.Result.IsFailure(out Exception ex))
            {
                //Cache function load errors to replay error messages on invoking failed functions
                _functionLoadErrors[loadResponse.FunctionId] = ex;
            }
            var inputBuffer = _functionInputBuffers[loadResponse.FunctionId];
            // link the invocation inputs to the invoke call
            var invokeBlock = new ActionBlock<ScriptInvocationContext>(ctx => SendInvocationRequest(ctx));
            var disposableLink = inputBuffer.LinkTo(invokeBlock);
            _inputLinks.Add(disposableLink);
        }

        internal void SendInvocationRequest(ScriptInvocationContext context)
        {
            try
            {
                if (_functionLoadErrors.ContainsKey(context.FunctionMetadata.FunctionId))
                {
                    _workerChannelLogger.LogDebug($"Function {context.FunctionMetadata.Name} failed to load");
                    context.ResultSource.TrySetException(_functionLoadErrors[context.FunctionMetadata.FunctionId]);
                    _executingInvocations.TryRemove(context.ExecutionContext.InvocationId.ToString(), out ScriptInvocationContext _);
                }
                else
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        context.ResultSource.SetCanceled();
                        return;
                    }

                    var functionMetadata = context.FunctionMetadata;

                    InvocationRequest invocationRequest = new InvocationRequest()
                    {
                        FunctionId = functionMetadata.FunctionId,
                        InvocationId = context.ExecutionContext.InvocationId.ToString(),
                    };
                    foreach (var pair in context.BindingData)
                    {
                        if (pair.Value != null)
                        {
                            invocationRequest.TriggerMetadata.Add(pair.Key, pair.Value.ToRpc(_workerChannelLogger));
                        }
                    }
                    foreach (var input in context.Inputs)
                    {
                        invocationRequest.InputData.Add(new ParameterBinding()
                        {
                            Name = input.name,
                            Data = input.val.ToRpc(_workerChannelLogger)
                        });
                    }

                    _executingInvocations.TryAdd(invocationRequest.InvocationId, context);

                    SendStreamingMessage(new StreamingMessage
                    {
                        InvocationRequest = invocationRequest
                    });
                }
            }
            catch (Exception invokeEx)
            {
                context.ResultSource.TrySetException(invokeEx);
            }
        }

        internal void InvokeResponse(InvocationResponse invokeResponse)
        {
            if (_executingInvocations.TryRemove(invokeResponse.InvocationId, out ScriptInvocationContext context)
                && invokeResponse.Result.IsSuccess(context.ResultSource))
            {
                IDictionary<string, object> bindingsDictionary = invokeResponse.OutputData
                    .ToDictionary(binding => binding.Name, binding => binding.Data.ToObject());

                var result = new ScriptInvocationResult()
                {
                    Outputs = bindingsDictionary,
                    Return = invokeResponse?.ReturnValue?.ToObject()
                };
                context.ResultSource.SetResult(result);
            }
        }

        internal void Log(RpcEvent msg)
        {
            var rpcLog = msg.Message.RpcLog;
            LogLevel logLevel = (LogLevel)rpcLog.Level;
            if (_executingInvocations.TryGetValue(rpcLog.InvocationId, out ScriptInvocationContext context))
            {
                // Restore the execution context from the original invocation. This allows AsyncLocal state to flow to loggers.
                System.Threading.ExecutionContext.Run(context.AsyncExecutionContext, (s) =>
                {
                    if (rpcLog.Exception != null)
                    {
                        var exception = new RpcException(rpcLog.Message, rpcLog.Exception.Message, rpcLog.Exception.StackTrace);
                        context.Logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, exception, (state, exc) => state);
                    }
                    else
                    {
                        context.Logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, null, (state, exc) => state);
                    }
                }, null);
            }
        }

        internal void HandleWorkerError(Exception exc)
        {
            _eventManager.Publish(new WorkerErrorEvent(_workerConfig.Language, Id, exc));
        }

        private void SendStreamingMessage(StreamingMessage msg)
        {
            _eventManager.Publish(new OutboundEvent(_workerId, msg));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startLatencyMetric?.Dispose();
                    _startSubscription?.Dispose();

                    // unlink function inputs
                    foreach (var link in _inputLinks)
                    {
                        link.Dispose();
                    }

                    foreach (var sub in _eventSubscriptions)
                    {
                        sub.Dispose();
                    }
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
