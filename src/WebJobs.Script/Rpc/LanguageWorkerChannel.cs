// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
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
    public class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly TimeSpan processStartTimeout = TimeSpan.FromSeconds(40);
        private readonly TimeSpan workerInitTimeout = TimeSpan.FromSeconds(30);
        private IScriptEventManager _eventManager;
        private IWorkerProcessFactory _processFactory;
        private IProcessRegistry _processRegistry;
        private IObservable<FunctionRegistrationContext> _functionRegistrations;
        private WorkerConfig _workerConfig;
        private Uri serverUri;
        private IRpcServer rpcServer;
        private RpcEvent _initEvent;
        private ILogger _workerChannelLogger;
        private ILogger _userLogsConsoleLogger;
        private bool _disposed;
        private string _workerId;
        private Process _process;
        private Queue<string> _processStdErrDataQueue = new Queue<string>(3);
        private int _maxNumberOfErrorMessages = 3;
        private IDictionary<string, BufferBlock<ScriptInvocationContext>> _functionInputBuffers = new Dictionary<string, BufferBlock<ScriptInvocationContext>>();
        private IDictionary<string, Exception> _functionLoadErrors = new Dictionary<string, Exception>();
        private ConcurrentDictionary<string, ScriptInvocationContext> _executingInvocations = new ConcurrentDictionary<string, ScriptInvocationContext>();
        private IObservable<InboundEvent> _inboundWorkerEvents;
        private List<IDisposable> _inputLinks = new List<IDisposable>();
        private List<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private IDisposable _startSubscription;
        // private IDisposable _startLatencyMetric;

        private JsonSerializerSettings _verboseSerializerSettings = new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter>()
                    {
                        new StringEnumConverter()
                    }
        };

        internal LanguageWorkerChannel()
        {
            // To help with unit tests
        }

        public LanguageWorkerChannel(
            IScriptEventManager eventManager,
            ILogger logger,
            IWorkerProcessFactory processFactory,
            IProcessRegistry processRegistry,
            WorkerConfig workerConfig,
            string workerId,
            IRpcServer rpcServer)
        {
            _workerId = workerId;
            _eventManager = eventManager;
            _processFactory = processFactory;
            _processRegistry = processRegistry;
            _workerConfig = workerConfig;
            ServerUri = rpcServer.Uri;
            _workerChannelLogger = logger;
            _userLogsConsoleLogger = logger;

            _workerChannelLogger.LogInformation("Init LanguageWorkerChannel");

            _inboundWorkerEvents = _eventManager.OfType<InboundEvent>()
                .Where(msg => msg.WorkerId == _workerId);

            _eventSubscriptions.Add(_inboundWorkerEvents
                .Where(msg => msg.MessageType == MsgType.RpcLog)
                .Subscribe(Log));

            _eventSubscriptions.Add(_eventManager.OfType<RpcEvent>()
                .Where(msg => msg.WorkerId == _workerId)
                    .Subscribe(msg =>
                    {
                        var jsonMsg = JsonConvert.SerializeObject(msg, _verboseSerializerSettings);
                        _userLogsConsoleLogger.LogDebug(jsonMsg);
                    }));

            _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                .Where(msg => Config.Extensions.Contains(Path.GetExtension(msg.FileChangeArguments.FullPath)))
                .Throttle(TimeSpan.FromMilliseconds(300)) // debounce
                .Subscribe(msg => _eventManager.Publish(new HostRestartEvent())));

            //InitializeWorker();
        }

        public string Id => _workerId;

        public WorkerConfig Config => _workerConfig;

        internal Queue<string> ProcessStdErrDataQueue => _processStdErrDataQueue;

        internal Uri ServerUri { get => serverUri; set => serverUri = value; }

        public IRpcServer RpcServer { get => rpcServer; set => rpcServer = value; }

        public RpcEvent InitEvent { get => _initEvent; }

        public void SetupLanguageWorkerChannel(ILogger logger)
        {
            _workerChannelLogger = logger;
            _userLogsConsoleLogger = logger;
        }

        public void StartWorkerProcess(string scriptRootPath)
        {
            var workerContext = new WorkerCreateContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                MaxMessageLength = 32 * 1024,
                WorkerId = _workerId,
                Arguments = _workerConfig.Arguments,
                WorkingDirectory = scriptRootPath,
                ServerUri = ServerUri,
            };

            _process = _processFactory.CreateWorkerProcess(workerContext);
            StartProcess();
            _processRegistry?.Register(_process);
        }

        internal void StartProcess()
        {
            _process.ErrorDataReceived += (sender, e) => OnErrorDataReceived(sender, e);
            _process.OutputDataReceived += (sender, e) => OnOutputDataReceived(sender, e);
            _process.Exited += (sender, e) => OnProcessExited(sender, e);
            _process.EnableRaisingEvents = true;

            _workerChannelLogger?.LogInformation($"Starting language worker process:{_process.StartInfo.FileName} {_process.StartInfo.Arguments}");
            _process.Start();
            _workerChannelLogger?.LogInformation($"{_process.StartInfo.FileName} process with Id={_process.Id} started");

            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                string msg = e.Data;
                if (IsLanguageWorkerConsoleLog(msg))
                {
                    msg = RemoveLogPrefix(msg);
                    _workerChannelLogger?.LogInformation(msg);
                }
                else
                {
                    _userLogsConsoleLogger?.LogInformation(msg);
                }
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            string exceptionMessage = string.Join(",", _processStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            bool workerErrorHandled = false;
            try
            {
                if (_process.ExitCode != 0)
                {
                    var processExitEx = new LanguageWorkerProcessExitException($"{_process.StartInfo.FileName} exited with code {_process.ExitCode}\n {exceptionMessage}");
                    HandleWorkerError(processExitEx);
                    workerErrorHandled = true;
                }
                else
                {
                    _process.WaitForExit();
                    _process.Close();
                }
            }
            catch (Exception ex)
            {
                if (!workerErrorHandled)
                {
                    var processExitEx = new LanguageWorkerProcessExitException($"Worker process is not attached. {exceptionMessage}", ex);
                    HandleWorkerError(processExitEx);
                }
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            // TODO: per language stdout/err parser?
            if (e.Data != null)
            {
                string msg = e.Data;
                if (msg.IndexOf("warn", StringComparison.OrdinalIgnoreCase) > -1)
                {
                    if (IsLanguageWorkerConsoleLog(msg))
                    {
                        msg = RemoveLogPrefix(msg);
                        _workerChannelLogger?.LogWarning(msg);
                    }
                    else
                    {
                        _userLogsConsoleLogger?.LogInformation(msg);
                    }
                }
                else if ((msg.IndexOf("error", StringComparison.OrdinalIgnoreCase) > -1) ||
                          (msg.IndexOf("fail", StringComparison.OrdinalIgnoreCase) > -1) ||
                          (msg.IndexOf("severe", StringComparison.OrdinalIgnoreCase) > -1))
                {
                    if (IsLanguageWorkerConsoleLog(msg))
                    {
                        msg = RemoveLogPrefix(msg);
                        _workerChannelLogger?.LogError(msg);
                    }
                    else
                    {
                        _userLogsConsoleLogger?.LogInformation(msg);
                    }
                    AddStdErrMessage(Sanitizer.Sanitize(msg));
                }
                else
                {
                    if (IsLanguageWorkerConsoleLog(msg))
                    {
                        msg = RemoveLogPrefix(msg);
                        _workerChannelLogger?.LogInformation(msg);
                    }
                    else
                    {
                        _userLogsConsoleLogger?.LogInformation(msg);
                    }
                }
            }
        }

        internal void AddStdErrMessage(string msg)
        {
            if (_processStdErrDataQueue.Count >= _maxNumberOfErrorMessages)
            {
                _processStdErrDataQueue.Dequeue();
                _processStdErrDataQueue.Enqueue(msg);
            }
            else
            {
                _processStdErrDataQueue.Enqueue(msg);
            }
        }

        internal bool IsLanguageWorkerConsoleLog(string msg)
        {
            if (msg.StartsWith(LanguageWorkerConstants.LanguageWorkerConsoleLogPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        internal string RemoveLogPrefix(string msg)
        {
            return Regex.Replace(msg, LanguageWorkerConstants.LanguageWorkerConsoleLogPrefix, string.Empty, RegexOptions.IgnoreCase);
        }

        // send capabilities to worker, wait for WorkerInitResponse
        public void InitializeWorker()
        {
            RpcEvent startEvent = _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.StartStream).FirstOrDefault();
            _startSubscription = _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.WorkerInitResponse)
                .Timeout(workerInitTimeout)
                .Take(1)
                .Subscribe(SetWorkerInitEvent, HandleWorkerError);

            Send(new StreamingMessage
            {
                WorkerInitRequest = new WorkerInitRequest()
                {
                    HostVersion = ScriptHost.Version
                }
            });
        }

        internal void SetWorkerInitEvent(RpcEvent initEvent)
        {
            _initEvent = initEvent;
        }

        public void WorkerReady(IObservable<FunctionRegistrationContext> functionRegistrations)
        {
            //_startLatencyMetric.Dispose();
            //_startLatencyMetric = null;
            _functionRegistrations = functionRegistrations;
            var initMessage = _initEvent.Message.WorkerInitResponse;
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
            if (loadResponse.Result.IsFailure(out Exception ex))
            {
                //Cache function load errors to replay error messages on invoking failed functions
                _functionLoadErrors[loadResponse.FunctionId] = ex;
            }
            var inputBuffer = _functionInputBuffers[loadResponse.FunctionId];
            // link the invocation inputs to the invoke call
            var invokeBlock = new ActionBlock<ScriptInvocationContext>(ctx => Invoke(ctx));
            var disposableLink = inputBuffer.LinkTo(invokeBlock);
            _inputLinks.Add(disposableLink);
        }

        public void Invoke(ScriptInvocationContext context)
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
                            invocationRequest.TriggerMetadata.Add(pair.Key, pair.Value.ToRpc());
                        }
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
            else
            {
                _workerChannelLogger.Log(logLevel, new EventId(0, rpcLog.EventId), rpcLog.Message, null, (state, exc) => state);
            }
        }

        internal void HandleWorkerError(Exception exc)
        {
            // The subscriber of WorkerErrorEvent is expected to Dispose() the errored channel
            _workerChannelLogger.LogError(exc, $"Language Worker Process exited.", _process.StartInfo.FileName);
            _eventManager.Publish(new WorkerErrorEvent(Id, exc));
        }

        private void Send(StreamingMessage msg)
        {
            _eventManager.Publish(new OutboundEvent(_workerId, msg));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // _startLatencyMetric?.Dispose();
                    _startSubscription?.Dispose();

                    // unlink function inputs
                    foreach (var link in _inputLinks)
                    {
                        link.Dispose();
                    }

                    // best effort process disposal
                    try
                    {
                        if (_process != null)
                        {
                            if (!_process.HasExited)
                            {
                                _process.Kill();
                                _process.WaitForExit();
                            }
                            _process.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        _workerChannelLogger.LogError(e, "LanguageWorkerChannel Dispose failure");
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
