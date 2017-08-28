﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;

using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly TimeSpan timeoutStart = TimeSpan.FromSeconds(20);
        private readonly TimeSpan timeoutInit = TimeSpan.FromSeconds(20);
        private readonly ScriptHostConfiguration _scriptConfig;
        private readonly IScriptEventManager _eventManager;
        private readonly IWorkerProcessFactory _processFactory;
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

        private bool disposedValue;

        public LanguageWorkerChannel(
            ScriptHostConfiguration scriptConfig,
            IScriptEventManager eventManager,
            IWorkerProcessFactory processFactory,
            IObservable<FunctionRegistrationContext> functionRegistrations,
            WorkerConfig workerConfig,
            Uri serverUri,
            ILogger logger)
        {
            _scriptConfig = scriptConfig;
            _eventManager = eventManager;
            _processFactory = processFactory;
            _functionRegistrations = functionRegistrations;
            _workerConfig = workerConfig;
            _serverUri = serverUri;
            _logger = logger;
            _workerId = Guid.NewGuid().ToString();

            _inboundWorkerEvents = _eventManager.OfType<InboundEvent>()
                .Where(msg => msg.WorkerId == _workerId);

            StartWorker();
        }

        public string Id => _workerId;

        public WorkerConfig Config => _workerConfig;

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

        public void Register(FunctionRegistrationContext context)
        {
            FunctionMetadata metadata = context.Metadata;
            _functionInputBuffers[context.Metadata.FunctionId] = context.InputBuffer;
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

        internal void InitWorker(RpcEvent startEvent)
        {
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

        internal void HandleWorkerError(Exception exc)
        {
            // unlink function inputs
            foreach (var link in _inputLinks)
            {
                link.Dispose();
            }
            _logger.LogError($"Worker {_workerId} encountered an error.", exc);
            _eventManager.Publish(new WorkerErrorEvent(this, exc));
        }

        internal void WorkerReady(RpcEvent initEvent)
        {
            var initMessage = initEvent.Message.WorkerInitResponse;
            if (initMessage.Result.IsFailure(out Exception exc))
            {
                HandleWorkerError(exc);
                return;
            }

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

        internal void LoadResponse(FunctionLoadResponse loadResponse)
        {
            if (loadResponse.Result.IsFailure(out Exception e))
            {
                _logger.LogError($"Function {loadResponse.FunctionId} failed to load", e);

                // load retry?
            }
            else
            {
                var inputBuffer = _functionInputBuffers[loadResponse.FunctionId];
                var invokeBlock = new ActionBlock<ScriptInvocationContext>(ctx => Invoke(ctx));
                var disposableLink = inputBuffer.LinkTo(invokeBlock);
                _inputLinks.Add(disposableLink);
            }
        }

        internal void StartWorker()
        {
            _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.StartStream)
                .Timeout(timeoutStart)
                .Take(1)
                .Subscribe(InitWorker, HandleWorkerError);

            var workerContext = new WorkerCreateContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                WorkerId = _workerId,
                WorkerConfig = _workerConfig,
                WorkingDirectory = _scriptConfig.RootScriptPath,
                Logger = _logger,
                ServerUri = _serverUri,
            };

            _process = _processFactory.CreateWorkerProcess(workerContext);
            StartProcess(_workerId, _process);
        }

        internal void StartProcess(string workerId, Process process)
        {
            process.ErrorDataReceived += (sender, e) =>
            {
                _logger.LogError(e?.Data);
            };
            process.OutputDataReceived += (sender, e) =>
            {
                _logger.LogInformation(e?.Data);
            };
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                if (process.ExitCode != 0)
                {
                    HandleWorkerError(new Exception($"Worker process with pid {process.Id} exited with code {process.ExitCode}"));
                }
                process.WaitForExit();
                process.Close();
            };

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
                    _process?.Kill();
                    _process?.Dispose();
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
