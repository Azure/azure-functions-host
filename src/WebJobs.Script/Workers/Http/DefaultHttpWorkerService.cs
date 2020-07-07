// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public class DefaultHttpWorkerService : IHttpWorkerService
    {
        private readonly HttpClient _httpClient;
        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly ILogger _logger;

        public DefaultHttpWorkerService(IOptions<HttpWorkerOptions> httpWorkerOptions, ILoggerFactory loggerFactory)
            : this(new HttpClient(), httpWorkerOptions, loggerFactory.CreateLogger<DefaultHttpWorkerService>())
        {
        }

        internal DefaultHttpWorkerService(HttpClient httpClient, IOptions<HttpWorkerOptions> httpWorkerOptions, ILogger logger)
        {
            _httpClient = httpClient;
            _httpWorkerOptions = httpWorkerOptions.Value;
            _logger = logger;
        }

        public Task InvokeAsync(ScriptInvocationContext scriptInvocationContext)
        {
            if (scriptInvocationContext.FunctionMetadata.IsHttpInAndOutFunction())
            {
                // type is empty for httpWorker section. EnableForwardingHttpRequest is opt-in for custom handler section.
                if (_httpWorkerOptions.Type == CustomHandlerType.None || _httpWorkerOptions.EnableForwardingHttpRequest)
                {
                    return ProcessHttpInAndOutInvocationRequest(scriptInvocationContext);
                }
                return ProcessDefaultInvocationRequest(scriptInvocationContext);
            }
            return ProcessDefaultInvocationRequest(scriptInvocationContext);
        }

        internal async Task ProcessHttpInAndOutInvocationRequest(ScriptInvocationContext scriptInvocationContext)
        {
            _logger.LogDebug("Will invoke simple httpTrigger function: '{functionName}' invocationId: '{invocationId}'", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);

            ScriptInvocationResult scriptInvocationResult = new ScriptInvocationResult()
            {
                Outputs = new Dictionary<string, object>()
            };

            (string name, DataType type, object request) input = scriptInvocationContext.Inputs.First();

            HttpRequest httpRequest = input.request as HttpRequest;
            if (httpRequest == null)
            {
                throw new InvalidOperationException($"HttpTrigger value for: `{input.name}` is null");
            }

            try
            {
                string pathValue = scriptInvocationContext.FunctionMetadata.Name;
                if (_httpWorkerOptions.EnableForwardingHttpRequest && _httpWorkerOptions.Type == CustomHandlerType.Http)
                {
                    pathValue = httpRequest.GetRequestUri().AbsolutePath;
                }

                using (HttpRequestMessage httpRequestMessage = await httpRequest.GetProxyHttpRequest(BuildAndGetUri(pathValue), scriptInvocationContext.ExecutionContext.InvocationId.ToString()))
                {
                    _logger.LogDebug("Sending http request message for simple httpTrigger function: '{functionName}' invocationId: '{invocationId}'", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
                    HttpResponseMessage invocationResponse = await _httpClient.SendAsync(httpRequestMessage);
                    _logger.LogDebug("Received http response for simple httpTrigger function: '{functionName}' invocationId: '{invocationId}'", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
                    BindingMetadata httpOutputBinding = scriptInvocationContext.FunctionMetadata.OutputBindings.FirstOrDefault();
                    if (httpOutputBinding != null)
                    {
                        // handle http output binding
                        scriptInvocationResult.Outputs.Add(httpOutputBinding.Name, invocationResponse);
                        // handle $return
                        scriptInvocationResult.Return = invocationResponse;
                    }
                    scriptInvocationContext.ResultSource.SetResult(scriptInvocationResult);
                }
            }
            catch (Exception responseEx)
            {
                scriptInvocationContext.ResultSource.TrySetException(responseEx);
            }
        }

        internal async Task ProcessDefaultInvocationRequest(ScriptInvocationContext scriptInvocationContext)
        {
            try
            {
                HttpScriptInvocationContext httpScriptInvocationContext = await scriptInvocationContext.ToHttpScriptInvocationContext();
                // Build httpRequestMessage from scriptInvocationContext

                using (HttpRequestMessage httpRequestMessage = await scriptInvocationContext.ToProxyHttpRequest(BuildAndGetUri(scriptInvocationContext.FunctionMetadata.Name), httpScriptInvocationContext))
                {
                    _logger.LogDebug("Sending http request for function:{functionName} invocationId:{invocationId}", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
                    HttpResponseMessage response = await _httpClient.SendAsync(httpRequestMessage);
                    _logger.LogDebug("Received http request for function:{functionName} invocationId:{invocationId}", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);

                    // Only process output bindings if response is succeess code
                    response.EnsureSuccessStatusCode();

                    HttpScriptInvocationResult httpScriptInvocationResult = await response.Content.ReadAsAsync<HttpScriptInvocationResult>();

                    if (httpScriptInvocationResult != null)
                    {
                        if (httpScriptInvocationResult.Outputs == null || !httpScriptInvocationResult.Outputs.Any())
                        {
                            _logger.LogWarning("Outputs not set on http response for invocationId:{invocationId}", scriptInvocationContext.ExecutionContext.InvocationId);
                        }
                        if (httpScriptInvocationResult.ReturnValue == null)
                        {
                            _logger.LogWarning("ReturnValue not set on http response for invocationId:{invocationId}", scriptInvocationContext.ExecutionContext.InvocationId);
                        }

                        ProcessLogsFromHttpResponse(scriptInvocationContext, httpScriptInvocationResult);

                        ScriptInvocationResult scriptInvocationResult = httpScriptInvocationResult.ToScriptInvocationResult(scriptInvocationContext);
                        scriptInvocationContext.ResultSource.SetResult(scriptInvocationResult);
                    }
                }
            }
            catch (Exception responseEx)
            {
                scriptInvocationContext.ResultSource.TrySetException(responseEx);
            }
        }

        internal void ProcessLogsFromHttpResponse(ScriptInvocationContext scriptInvocationContext, HttpScriptInvocationResult invocationResult)
        {
            if (scriptInvocationContext == null)
            {
                throw new ArgumentNullException(nameof(scriptInvocationContext));
            }
            if (invocationResult == null)
            {
                throw new ArgumentNullException(nameof(invocationResult));
            }
            if (invocationResult.Logs != null)
            {
                // Restore the execution context from the original invocation. This allows AsyncLocal state to flow to loggers.
                System.Threading.ExecutionContext.Run(scriptInvocationContext.AsyncExecutionContext, (s) =>
                {
                    foreach (var userLog in invocationResult.Logs)
                    {
                        scriptInvocationContext.Logger?.LogInformation(userLog);
                    }
                }, null);
            }
        }

        public async Task<bool> IsWorkerReady(CancellationToken cancellationToken)
        {
            bool continueWaitingForWorker = await Utility.DelayAsync(WorkerConstants.WorkerInitTimeoutSeconds, WorkerConstants.WorkerReadyCheckPollingIntervalMilliseconds, async () =>
            {
                return await IsWorkerReadyForRequest();
            }, cancellationToken);
            return !continueWaitingForWorker;
        }

        private async Task<bool> IsWorkerReadyForRequest()
        {
            string requestUri = BuildAndGetUri();
            try
            {
                await SendRequest(requestUri);
                // Any Http response indicates a valid server Url
                return false;
            }
            catch (HttpRequestException httpRequestEx)
            {
                if (httpRequestEx.InnerException != null && httpRequestEx.InnerException is SocketException)
                {
                    // Wait for the worker to be ready
                    _logger.LogDebug("Waiting for HttpWorker to be initialized. Request to: {requestUri} failing with exception message: {message}", requestUri, httpRequestEx.Message);
                    return true;
                }
                // Any other inner exception, consider HttpWorker to be ready
                return false;
            }
        }

        private string BuildAndGetUri(string pathValue = null)
        {
            if (!string.IsNullOrEmpty(pathValue))
            {
                return new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port, pathValue).ToString();
            }
            return new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port).ToString();
        }

        private async Task<HttpResponseMessage> SendRequest(string requestUri, HttpMethod method = null)
        {
            HttpRequestMessage httpRequestMessage = new HttpRequestMessage();
            httpRequestMessage.RequestUri = new Uri(requestUri);
            if (method != null)
            {
                httpRequestMessage.Method = method;
            }

            return await _httpClient.SendAsync(httpRequestMessage);
        }

        public async Task PingAsync()
        {
            string requestUri = BuildAndGetUri();
            try
            {
                HttpResponseMessage response = await SendRequest(requestUri, HttpMethod.Get);
                _logger.LogDebug($"Response code while pinging uri '{requestUri}' is '{response.StatusCode}'");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Pinging uri '{requestUri}' resulted in exception", ex);
            }
        }
    }
}
