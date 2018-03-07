// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly TimeSpan timeoutStart = TimeSpan.FromSeconds(20);
        private readonly TimeSpan timeoutInit = TimeSpan.FromSeconds(20);
        private readonly ScriptHostConfiguration _scriptConfig;
        private readonly IScriptEventManager _eventManager;
        private readonly IWorkerProcessFactory _processFactory;
        private readonly IProcessRegistry _processRegistry;
        private readonly IObservable<FunctionRegistrationContext> _functionRegistrations;
        private readonly WorkerConfig _workerConfig;
        private readonly Uri _serverUri;
        private readonly ILogger _logger;
        private string _workerId;
        private Process _process;
        private IDictionary<string, BufferBlock<ScriptInvocationContext>> _functionInputBuffers = new Dictionary<string, BufferBlock<ScriptInvocationContext>>();
        private ConcurrentDictionary<string, ScriptInvocationContext> _executingInvocations = new ConcurrentDictionary<string, ScriptInvocationContext>();

        private IObservable<InboundEvent> _inboundWorkerEvents;

        private List<IDisposable> _inputLinks = new List<IDisposable>();
        private List<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private IDisposable _startSubscription;

        private JsonSerializerSettings _verboseSerializerSettings = new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter>()
                    {
                        new StringEnumConverter()
                    }
        };

        private bool disposedValue;

        public LanguageWorkerChannel(
            ScriptHostConfiguration scriptConfig,
            IScriptEventManager eventManager,
            IWorkerProcessFactory processFactory,
            IProcessRegistry processRegistry,
            IObservable<FunctionRegistrationContext> functionRegistrations,
            WorkerConfig workerConfig,
            Uri serverUri,
            ILoggerFactory loggerFactory)
        {
            _workerId = Guid.NewGuid().ToString();

            _scriptConfig = scriptConfig;
            _eventManager = eventManager;
            _processFactory = processFactory;
            _processRegistry = processRegistry;
            _functionRegistrations = functionRegistrations;
            _workerConfig = workerConfig;
            _serverUri = serverUri;

            _logger = loggerFactory.CreateLogger($"Worker.{workerConfig.Language}.{_workerId}");

            _inboundWorkerEvents = _eventManager.OfType<InboundEvent>()
                .Where(msg => msg.WorkerId == _workerId);

            _eventSubscriptions.Add(_inboundWorkerEvents
                .Where(msg => msg.MessageType == MsgType.RpcLog)
                .Subscribe(Log));

            if (scriptConfig.LogFilter.Filter("Worker", LogLevel.Trace))
            {
                _eventSubscriptions.Add(_eventManager.OfType<RpcEvent>()
                    .Where(msg => msg.WorkerId == _workerId)
                    .Subscribe(msg =>
                    {
                        var jsonMsg = JsonConvert.SerializeObject(msg, _verboseSerializerSettings);
                        _logger.LogTrace(jsonMsg);
                    }));
            }

            _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                .Where(msg => Path.GetExtension(msg.FileChangeArguments.FullPath) == Config.Extension)
                .Throttle(TimeSpan.FromMilliseconds(300)) // debounce
                .Subscribe(msg => _eventManager.Publish(new HostRestartEvent())));

            StartWorker();
        }

        public string Id => _workerId;

        public WorkerConfig Config => _workerConfig;

        // start worker process and wait for an rpc start stream response
        internal void StartWorker()
        {
            _startSubscription = _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.StartStream)
                .Timeout(timeoutStart)
                .Take(1)
                .Subscribe(InitWorker, HandleWorkerError);

            var workerContext = new WorkerCreateContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                WorkerId = _workerId,
                Arguments = _workerConfig.Arguments,
                WorkingDirectory = _scriptConfig.RootScriptPath,
                ServerUri = _serverUri,
            };

            _process = _processFactory.CreateWorkerProcess(workerContext);
            StartProcess(_workerId, _process);
        }

        // send capabilities to worker, wait for WorkerInitResponse
        internal void InitWorker(RpcEvent startEvent)
        {
            _processRegistry?.Register(_process);

            _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.WorkerInitResponse)
                .Timeout(timeoutInit)
                .Take(1)
                .Subscribe(WorkerReady, HandleWorkerError);

            Send(new StreamingMessage
            {
                WorkerInitRequest = new WorkerInitRequest()
                {
                    HostVersion = ScriptHost.Version
                }
            });
        }

        internal void WorkerReady(RpcEvent initEvent)
        {
            var initMessage = initEvent.Message.WorkerInitResponse;
            if (initMessage.Result.IsFailure(out Exception exc))
            {
                HandleWorkerError(exc);
                return;
            }

            // subscript to all function registrations in order to load functions
            _eventSubscriptions.Add(_functionRegistrations.Subscribe(Register));

            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.FunctionLoadResponse)
                .Subscribe((msg) => LoadResponse(msg.Message.FunctionLoadResponse)));

            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.InvocationResponse)
                .Subscribe((msg) => InvokeResponse(msg.Message.InvocationResponse)));

            _eventManager.Publish(new WorkerReadyEvent
            {
                Id = _workerId,
                Version = initMessage.WorkerVersion,
                Capabilities = initMessage.Capabilities,
                Config = _workerConfig,
            });
        }

        public void Register(FunctionRegistrationContext context)
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
                    Directory = metadata.FunctionDirectory,
                    EntryPoint = metadata.EntryPoint ?? string.Empty,
                    ScriptFile = metadata.ScriptFile ?? string.Empty
                }
            };

            foreach (var binding in metadata.Bindings)
            {
                request.Metadata.Bindings.Add(binding.Name, new BindingInfo
                {
                    Direction = (BindingInfo.Types.Direction)binding.Direction,
                    Type = binding.Type
                });
            }

            Send(new StreamingMessage
            {
                FunctionLoadRequest = request
            });
        }

        internal void LoadResponse(FunctionLoadResponse loadResponse)
        {
            if (loadResponse.Result.IsFailure(out Exception e))
            {
                _logger.LogError($"Function {loadResponse.FunctionId} failed to load", e);
            }
            else
            {
                var inputBuffer = _functionInputBuffers[loadResponse.FunctionId];

                // link the invocation inputs to the invoke call
                var invokeBlock = new ActionBlock<ScriptInvocationContext>(ctx => Invoke(ctx));
                var disposableLink = inputBuffer.LinkTo(invokeBlock);
                _inputLinks.Add(disposableLink);
            }
        }

        public void Invoke(ScriptInvocationContext context)
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
                invocationRequest.TriggerMetadata.Add(pair.Key, pair.Value.ToRpc());
            }
            foreach (var input in context.Inputs)
            {
                invocationRequest.InputData.Add(new ParameterBinding()
                {
                    Name = input.name,
                    Data = input.val.ToRpc()
                });
            }

            _executingInvocations.TryAdd(invocationRequest.InvocationId, context);

            Send(new StreamingMessage
            {
                InvocationRequest = invocationRequest
            });
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
                        var exception = new Rpc.RpcException(rpcLog.Message, rpcLog.Exception.Message, rpcLog.Exception.StackTrace);

                        context.Logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, exception, (state, exc) => state);

                        context.ResultSource.TrySetException(exception);
                        _executingInvocations.TryRemove(rpcLog.InvocationId, out ScriptInvocationContext _);
                    }
                    else
                    {
                        context.Logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, null, (state, exc) => state);
                    }
                }, null);
            }
            else
            {
                _logger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, null, (state, exc) => state);
            }
        }

        internal void HandleWorkerError(Exception exc)
        {
            _startSubscription?.Dispose();

            // unlink function inputs
            foreach (var link in _inputLinks)
            {
                link.Dispose();
            }

            _logger.LogError($"Worker encountered an error.", exc);
            _eventManager.Publish(new WorkerErrorEvent(this, exc));
        }

        // TODO: move this out of LanguageWorkerChannel to WorkerProcessFactory
        internal void StartProcess(string workerId, Process process)
        {
            process.ErrorDataReceived += (sender, e) =>
            {
                // Java logs to stderr by default
                // TODO: per language stdout/err parser?
                if (e.Data != null)
                {
                    _logger.LogInformation(e.Data);
                }
            };
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogInformation(e.Data);
                }
            };
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                try
                {
                    if (process.ExitCode != 0)
                    {
                        HandleWorkerError(new Exception($"Worker process with pid {process.Id} exited with code {process.ExitCode}"));
                    }
                    process.WaitForExit();
                    process.Close();
                }
                catch
                {
                    HandleWorkerError(new Exception("Worker process is not attached"));
                }
            };

            _logger.LogInformation($"Start Process: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        private void Send(StreamingMessage msg)
        {
            _eventManager.Publish(new OutboundEvent(_workerId, msg));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // best effort process disposal
                    try
                    {
                        _process?.Kill();
                        _process?.Dispose();
                    }
                    catch
                    {
                    }

                    foreach (var sub in _eventSubscriptions)
                    {
                        sub.Dispose();
                    }
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
