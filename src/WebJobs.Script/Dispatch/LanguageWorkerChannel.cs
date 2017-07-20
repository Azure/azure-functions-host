// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // TODO: move to RPC project?
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly LanguageWorkerConfig _workerConfig;
        private readonly ScriptHostConfiguration _scriptConfig;
        private readonly TraceWriter _logger;
        private TraceWriter _userTraceWriter;
        private Process _process;
        private IObservable<ChannelContext> _connections;
        private ChannelContext _context;
        private ChannelState _state;
        private IDictionary<FunctionMetadata, Task<FunctionLoadResponse>> _functionLoadState = new Dictionary<FunctionMetadata, Task<FunctionLoadResponse>>();

        public LanguageWorkerChannel(ScriptHostConfiguration scriptConfig, LanguageWorkerConfig workerConfig, TraceWriter logger, IObservable<ChannelContext> connections)
        {
            _workerConfig = workerConfig;
            _scriptConfig = scriptConfig;
            _logger = logger;
            _connections = connections;
            _state = ChannelState.Stopped;
        }

        public ChannelState State { get => _state; set => _state = value; }

        public async Task<object> InvokeAsync(FunctionMetadata functionMetadata, Dictionary<string, object> scriptExecutionContext)
        {
            _userTraceWriter = (TraceWriter)scriptExecutionContext["traceWriter"];
            FunctionLoadResponse loadResponse = await _functionLoadState.GetOrAdd(functionMetadata, metadata => LoadInternalAsync(metadata));
            scriptExecutionContext.Add("functionId", functionMetadata.FunctionId);
            InvocationRequest invocationRequest = scriptExecutionContext.ToRpcInvocationRequest();
            object result = null;
            InvocationResponse invocationResponse = await _context.InvokeAsync(invocationRequest);
            Dictionary<string, object> itemsDictionary = new Dictionary<string, object>();
            if (invocationResponse.OutputData?.Count > 0)
            {
                foreach (ParameterBinding outputParameterBinding in invocationResponse.OutputData)
                {
                    object objValue = outputParameterBinding.Data.FromRpcTypedDataToObject();
                    if (outputParameterBinding.Name == "$return")
                    {
                        result = objValue;
                    }
                    else
                    {
                        itemsDictionary.Add(outputParameterBinding.Name, objValue);
                    }
                }
            }
            Dictionary<string, object> bindingsDictionary = (Dictionary<string, object>)scriptExecutionContext["bindings"];
            bindingsDictionary.AddRange(itemsDictionary);
            scriptExecutionContext["bindings"] = bindingsDictionary;
            return result;
        }

        public async Task HandleFileEventAsync(FileSystemEventArgs fileEvent)
        {
            // FileChangeEventRequest request = new FileChangeEventRequest();
            // FileChangeEventResponse response = await _context.LoadAsync(request);
            await Task.CompletedTask;
        }

        protected Task<FunctionLoadResponse> LoadInternalAsync(FunctionMetadata functionMetadata)
        {
            FunctionLoadRequest request = new FunctionLoadRequest()
            {
                FunctionId = Guid.NewGuid().ToString(),
                Metadata = functionMetadata.ToRpcFunctionMetadata()
            };
            functionMetadata.FunctionId = request.FunctionId;
            return _context.LoadAsync(request);
        }

        public void LoadAsync(FunctionMetadata functionMetadata)
        {
            _functionLoadState[functionMetadata] = LoadInternalAsync(functionMetadata);
        }

        public async Task StartAsync()
        {
            await StopAsync();

            string requestId = Guid.NewGuid().ToString();

            TaskCompletionSource<bool> connectionSource = new TaskCompletionSource<bool>();
            IDisposable subscription = null;
            subscription = _connections
                .Where(msg => msg.RequestId == requestId)
                .Timeout(TimeSpan.FromSeconds(10))
                .Subscribe(msg =>
                {
                    _context = msg;
                    connectionSource.SetResult(true);
                    subscription?.Dispose();
                    _state = ChannelState.Connected;
                    _context.InputStream.Subscribe(HandleLogs);
                }, exc =>
                {
                    _state = ChannelState.Faulted;
                    connectionSource.SetException(exc);
                    subscription?.Dispose();
                });
            Utilities.IsTcpEndpointAvailable("127.0.0.1", 50051, _logger);

            Task startWorkerTask = StartWorkerAsync(_workerConfig, requestId);

            await Task.WhenAny(startWorkerTask, connectionSource.Task);
        }

        private void HandleLogs(StreamingMessage msg)
        {
            // TODO figure out live logging
            if (msg.ContentCase == StreamingMessage.ContentOneofCase.RpcLog)
            {
                var logMessage = msg.RpcLog;

                // TODO get rest of the properties from log message
                string message = logMessage.Message;
                if (message != null)
                {
                    try
                    {
                        // TODO Initialize SystemTraceWriter
                        LogLevel logLevel = (LogLevel)logMessage.Level;
                        TraceLevel level = TraceLevel.Off;
                        switch (logLevel)
                        {
                            case LogLevel.Critical:
                            case LogLevel.Error:
                                level = TraceLevel.Error;
                                break;

                            case LogLevel.Trace:
                            case LogLevel.Debug:
                                level = TraceLevel.Verbose;
                                break;

                            case LogLevel.Information:
                                level = TraceLevel.Info;
                                break;

                            case LogLevel.Warning:
                                level = TraceLevel.Warning;
                                break;

                            default:
                                break;
                        }

                        // _logger.Trace(new TraceEvent(level, message));
                        _userTraceWriter.Trace(new TraceEvent(level, message));
                    }
                    catch (ObjectDisposedException)
                    {
                        // if a function attempts to write to a disposed
                        // TraceWriter. Might happen if a function tries to
                        // log after calling done()
                    }
                }
            }
        }

        public Task StopAsync()
        {
            // TODO: send cancellation warning
            // TODO: Close request stream for each worker pool
            _process?.Kill();
            _process = null;
            return Task.CompletedTask;
        }

        internal Task StartWorkerAsync(LanguageWorkerConfig config, string requestId)
        {
            var tcs = new TaskCompletionSource<bool>();
            _state = ChannelState.Started;

            try
            {
                List<string> output = new List<string>();

                var startInfo = new ProcessStartInfo
                {
                    FileName = config.ExecutablePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = _scriptConfig.RootScriptPath,
                    Arguments = config.ToArgumentString(requestId)
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
                    _process.WaitForExit();
                    _process.Close();
                    _state = ChannelState.Stopped;
                    tcs.SetResult(true);
                };

                _process.Start();

                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                _logger.Error("Error starting LanguageWorkerChannel", exc);
                _state = ChannelState.Faulted;
                tcs.SetException(exc);
            }
            return tcs.Task;
        }

        public void Dispose()
        {
            _process?.Kill();
        }
    }
}
