﻿// Copyright (c) .NET Foundation. All rights reserved.
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
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpWorkerOptions = httpWorkerOptions.Value ?? throw new ArgumentNullException(nameof(httpWorkerOptions.Value));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                string uriPathValue = GetPathValue(_httpWorkerOptions, scriptInvocationContext.FunctionMetadata.Name, httpRequest);
                string uri = BuildAndGetUri(uriPathValue);

                using (HttpRequestMessage httpRequestMessage = httpRequest.ToHttpRequestMessage(uri))
                {
                    AddHeaders(httpRequestMessage, scriptInvocationContext.ExecutionContext.InvocationId.ToString());

                    _logger.LogDebug("Forwarding http request for httpTrigger function: '{functionName}' invocationId: '{invocationId}'", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
                    HttpResponseMessage invocationResponse = await _httpClient.SendAsync(httpRequestMessage);
                    _logger.LogDebug("Received http response for httpTrigger function: '{functionName}' invocationId: '{invocationId}'", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);

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

        internal void AddHeaders(HttpRequestMessage httpRequest, string invocationId)
        {
            httpRequest.Headers.Add(HttpWorkerConstants.HostVersionHeaderName, ScriptHost.Version);
            httpRequest.Headers.Add(HttpWorkerConstants.InvocationIdHeaderName, invocationId);
            httpRequest.Headers.UserAgent.ParseAdd($"{HttpWorkerConstants.UserAgentHeaderValue}/{ScriptHost.Version}");
        }

        internal string GetPathValue(HttpWorkerOptions httpWorkerOptions, string functionName, HttpRequest httpRequest)
        {
            string pathValue = functionName;
            if (httpWorkerOptions.EnableForwardingHttpRequest && httpWorkerOptions.Type == CustomHandlerType.Http)
            {
                pathValue = httpRequest.GetRequestUri().AbsolutePath;
            }

            return pathValue;
        }

        internal async Task ProcessDefaultInvocationRequest(ScriptInvocationContext scriptInvocationContext)
        {
            try
            {
                HttpScriptInvocationContext httpScriptInvocationContext = await scriptInvocationContext.ToHttpScriptInvocationContext();
                string uri = BuildAndGetUri(scriptInvocationContext.FunctionMetadata.Name);

                // Build httpRequestMessage from scriptInvocationContext
                using (HttpRequestMessage httpRequestMessage = httpScriptInvocationContext.ToHttpRequestMessage(uri))
                {
                    AddHeaders(httpRequestMessage, scriptInvocationContext.ExecutionContext.InvocationId.ToString());

                    _logger.LogDebug("Sending http request for function:{functionName} invocationId:{invocationId}", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
                    HttpResponseMessage response = await _httpClient.SendAsync(httpRequestMessage);
                    _logger.LogDebug("Received http response for function:{functionName} invocationId:{invocationId}", scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);

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
                string requestUri = new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port).ToString();
                HttpRequestMessage httpRequestMessage = new HttpRequestMessage();
                httpRequestMessage.RequestUri = new Uri(requestUri);
                try
                {
                    await _httpClient.SendAsync(httpRequestMessage);
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
            }, cancellationToken);

            return !continueWaitingForWorker;
        }

        internal string BuildAndGetUri(string pathValue = null)
        {
            if (string.IsNullOrEmpty(pathValue))
            {
                return new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port).ToString();
            }
            return new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port, pathValue).ToString();
        }
    }
}
