// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // TODO: move to RPC project?
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly TimeSpan timeoutStart = TimeSpan.FromSeconds(60);
        private readonly TimeSpan timeoutInit = TimeSpan.FromSeconds(60);
        private readonly TimeSpan timeoutLoad = TimeSpan.FromSeconds(60);
        private readonly TimeSpan timeoutInvoke = TimeSpan.FromMinutes(60);
        private readonly ScriptHostConfiguration _scriptConfig;
        private readonly IScriptEventManager _eventManager;
        private readonly IWorkerProcessFactory _processFactory;
        private readonly WorkerConfig _workerConfig;
        private readonly Uri _serverUri;
        private readonly ILogger _logger;
        private Process _process;
        private IDictionary<FunctionMetadata, Task<FunctionLoadResponse>> _functionLoadState = new Dictionary<FunctionMetadata, Task<FunctionLoadResponse>>();

        private AsyncLazy<WorkerInfo> _workerInfo;

        public LanguageWorkerChannel(ScriptHostConfiguration scriptConfig, IScriptEventManager eventManager, IWorkerProcessFactory processFactory, WorkerConfig workerConfig, Uri serverUri, ILogger logger)
        {
            _scriptConfig = scriptConfig;
            _eventManager = eventManager;
            _processFactory = processFactory;
            _workerConfig = workerConfig;
            _serverUri = serverUri;
            _logger = logger;
            _workerInfo = new AsyncLazy<WorkerInfo>((ct) => StartWorkerAsync(), new CancellationTokenSource());
        }

        private async Task SendAsync(StreamingMessage msg, string workerId = null)
        {
            workerId = workerId ?? (await _workerInfo).Id;
            var msgEvent = new RpcEvent(workerId, msg);
            _eventManager.Publish(msgEvent);
        }

        private async Task<StreamingMessage> ReceiveResponseAsync(string responseId, TimeSpan timeout)
        {
            TaskCompletionSource<StreamingMessage> tcs = new TaskCompletionSource<StreamingMessage>();
            IDisposable subscription = null;
            subscription = _eventManager
                .OfType<RpcEvent>()
                .Where(msg => msg.Origin == RpcEvent.MessageOrigin.Worker && msg.Message?.RequestId == responseId)
                .Timeout(timeout)
                .Subscribe(response =>
                {
                    tcs.SetResult(response.Message);
                    subscription?.Dispose();
                }, exc =>
                {
                    tcs.SetException(exc);
                    subscription?.Dispose();
                });
            return await tcs.Task;
        }

        private async Task<StreamingMessage> ReceiveResponseAsync(StreamingMessage request, TimeSpan timeout, string workerId = null)
        {
            request.RequestId = Guid.NewGuid().ToString();
            var receiveTask = ReceiveResponseAsync(request.RequestId, timeout);
            await SendAsync(request, workerId);
            return await receiveTask;
        }

        public async Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext context)
        {
            FunctionLoadResponse loadResponse = await _functionLoadState.GetOrAdd(functionMetadata, metadata => LoadAsync(metadata));

            InvocationRequest invocationRequest = new InvocationRequest()
            {
                FunctionId = loadResponse.FunctionId,
                InvocationId = context.ExecutionContext.InvocationId.ToString(),
            };

            foreach (var pair in context.BindingData)
            {
                invocationRequest.TriggerMetadata.Add(pair.Key, await pair.Value.ToRpcAsync().ConfigureAwait(false));
            }

            foreach (var input in context.Inputs)
            {
                invocationRequest.InputData.Add(new ParameterBinding()
                {
                    Name = input.name,

                    // TODO: get rid of this async. We can't handle async reading of streams (http body)
                    Data = await input.val.ToRpcAsync().ConfigureAwait(false)
                });
            }

            var response = await ReceiveResponseAsync(new StreamingMessage()
            {
                InvocationRequest = invocationRequest
            }, timeoutInvoke);
            InvocationResponse invocationResponse = response.InvocationResponse;
            invocationResponse.Result.VerifySuccess();

            IDictionary<string, object> bindingsDictionary = invocationResponse.OutputData
                    .ToDictionary(binding => binding.Name, binding => binding.Data.ToObject());

            return new ScriptInvocationResult()
            {
                Outputs = bindingsDictionary,
                Return = invocationResponse?.ReturnValue?.ToObject()
            };
        }

        public async Task HandleFileEventAsync(FileSystemEventArgs fileEvent)
        {
            FileChangeEventRequest request = new FileChangeEventRequest()
            {
                FullPath = fileEvent.FullPath,
                Name = fileEvent.Name,
                Type = (FileChangeEventRequest.Types.Type)fileEvent.ChangeType
            };

            await SendAsync(new StreamingMessage()
            {
                FileChangeEventRequest = request,
            });
        }

        private async Task<FunctionLoadResponse> LoadAsync(FunctionMetadata metadata)
        {
            FunctionLoadRequest request = new FunctionLoadRequest()
            {
                FunctionId = Guid.NewGuid().ToString(),
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

            var response = await ReceiveResponseAsync(new StreamingMessage()
            {
                FunctionLoadRequest = request
            }, timeoutLoad);

            response.FunctionLoadResponse.Result.VerifySuccess();
            return response.FunctionLoadResponse;
        }

        public void Register(FunctionMetadata functionMetadata)
        {
            _functionLoadState[functionMetadata] = LoadAsync(functionMetadata);
        }

        internal async Task<WorkerInfo> StartWorkerAsync()
        {
            await StopAsync();

            string workerId = Guid.NewGuid().ToString();
            string requestId = Guid.NewGuid().ToString();

            try
            {
                var startStreamTask = ReceiveResponseAsync(requestId, timeoutStart);

                var workerContext = new WorkerCreateContext()
                {
                    RequestId = requestId,
                    WorkerId = workerId,
                    WorkerConfig = _workerConfig,
                    WorkingDirectory = _scriptConfig.RootScriptPath,
                    Logger = _logger,
                    ServerUri = _serverUri,
                };

                _process = _processFactory.CreateWorkerProcess(workerContext);
                StartProcess(workerId, _process);
                await startStreamTask;

                StreamingMessage response = await ReceiveResponseAsync(new StreamingMessage()
                {
                    WorkerInitRequest = new WorkerInitRequest()
                    {
                        HostVersion = ScriptHost.Version
                    }
                }, timeoutInit, workerId);

                WorkerInitResponse initResponse = response.WorkerInitResponse;
                initResponse.Result.VerifySuccess();

                return new WorkerInfo(workerId, initResponse.WorkerVersion, initResponse.Capabilities);
            }
            catch (Exception exc)
            {
                _logger.LogError($"Worker {workerId} was unable to start", exc);
                throw;
            }
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
                if (process.ExitCode > 0)
                {
                    _logger.LogError($"Worker {workerId} pid {process.Id} exited with code {process.ExitCode}");
                }
                process.WaitForExit();
                process.Close();
                process.Dispose();
            };

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        public Task StopAsync()
        {
            // TODO: send cancellation warning
            // TODO: Close request stream for each worker pool
            _process?.Kill();
            _process = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _workerInfo.Dispose();
            _process?.Kill();
            _process?.Dispose();
        }
    }
}
