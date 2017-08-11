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
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;

using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

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
        private readonly LanguageWorkerConfig _workerConfig;
        private readonly TraceWriter _logger;
        private Process _process;
        private IDictionary<FunctionMetadata, Task<FunctionLoadResponse>> _functionLoadState = new Dictionary<FunctionMetadata, Task<FunctionLoadResponse>>();
        private int _port;

        private AsyncLazy<WorkerInfo> _workerInfo;

        public LanguageWorkerChannel(ScriptHostConfiguration scriptConfig, IScriptEventManager eventManager, LanguageWorkerConfig workerConfig, TraceWriter logger, int port)
        {
            _scriptConfig = scriptConfig;
            _eventManager = eventManager;
            _workerConfig = workerConfig;
            _logger = logger;
            _port = port;
            _workerInfo = new AsyncLazy<WorkerInfo>((ct) => StartWorkerAsync(), new CancellationTokenSource());
        }

        private async Task SendAsync(StreamingMessage msg, string workerId = null)
        {
            workerId = workerId ?? (await _workerInfo).Id;
            var msgEvent = new RpcEvent(workerId, msg);
            _eventManager.Publish(msgEvent);
        }

        // responseId == requestId
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

        private async Task<StreamingMessage> ReqResAsync(StreamingMessage request, TimeSpan timeout, string workerId = null)
        {
            request.RequestId = Guid.NewGuid().ToString();
            var receiveTask = ReceiveResponseAsync(request.RequestId, timeout);
            await SendAsync(request, workerId);
            return await receiveTask;
        }

        public async Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext context)
        {
            FunctionLoadResponse loadResponse = await _functionLoadState.GetOrAdd(functionMetadata, metadata => LoadInternalAsync(metadata));

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
                    Data = await input.val.ToRpcAsync().ConfigureAwait(false)
                });
            }

            var response = await ReqResAsync(new StreamingMessage()
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

        private async Task<FunctionLoadResponse> LoadInternalAsync(FunctionMetadata metadata)
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

            var response = await ReqResAsync(new StreamingMessage()
            {
                FunctionLoadRequest = request
            }, timeoutLoad);

            response.FunctionLoadResponse.Result.VerifySuccess();
            return response.FunctionLoadResponse;
        }

        public async Task StartAsync() => await _workerInfo;

        public void LoadAsync(FunctionMetadata functionMetadata)
        {
            _functionLoadState[functionMetadata] = LoadInternalAsync(functionMetadata);
        }

        internal async Task<WorkerInfo> StartWorkerAsync()
        {
            await StopAsync();

            string workerId = Guid.NewGuid().ToString();
            string requestId = Guid.NewGuid().ToString();

            var startStreamTask = ReceiveResponseAsync(requestId, timeoutStart);

            // throws if failure during process creation
            var startWorkerProcessTask = StartWorkerProcessAsync(_workerConfig, workerId, requestId);
            await Task.WhenAny(startStreamTask, startWorkerProcessTask);

            StreamingMessage response = await ReqResAsync(new StreamingMessage()
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

        internal async Task StartWorkerProcessAsync(LanguageWorkerConfig config, string workerId, string requestId)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            try
            {
                var startInfo = new ProcessStartInfo(config.ExecutablePath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = _scriptConfig.RootScriptPath,
                    Arguments = config.ToArgumentString(_port, workerId, requestId)
                };

                _process = new Process { StartInfo = startInfo };
                _process.ErrorDataReceived += (sender, e) =>
                {
                    _logger.Error(e?.Data);
                };
                _process.OutputDataReceived += (sender, e) =>
                {
                    _logger.Info(e?.Data);
                };
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) =>
                {
                    if (_process.ExitCode > 0)
                    {
                        tcs.TrySetException(new InvalidOperationException($"Worker process exited with code ${_process.ExitCode}"));
                    }
                    _process.WaitForExit();
                    _process.Close();
                    _process.Dispose();
                };

                _process.Start();

                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                _logger.Error("Error starting LanguageWorkerChannel", exc);
                tcs.SetException(exc);
            }
            await tcs.Task;
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
