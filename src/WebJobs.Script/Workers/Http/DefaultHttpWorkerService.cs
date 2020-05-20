// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.AspNetCore.WebUtilities;
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
            if (scriptInvocationContext.FunctionMetadata.IsHttpInAndOutFunction() && _httpWorkerOptions.EnableHttpRequestForward)
            {
                return ProcessHttpInAndOutInvocationRequest(scriptInvocationContext);
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

            HttpRequestMessage httpRequestMessage = null;

            (string name, DataType type, object request) input = scriptInvocationContext.Inputs.First();

            HttpRequest httpRequest = input.request as HttpRequest;
            if (httpRequest == null)
            {
                throw new InvalidOperationException($"HttpTrigger value for: `{input.name}` is null");
            }

            try
            {
                // Build HttpRequestMessage from HttpTrigger binding
                HttpRequestMessageFeature httpRequestMessageFeature = new HttpRequestMessageFeature(httpRequest.HttpContext);
                httpRequestMessage = httpRequestMessageFeature.HttpRequestMessage;

                AddRequestHeadersAndSetRequestUri(httpRequestMessage, scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId.ToString());

                // Populate query params from httpTrigger
                string httpWorkerUri = QueryHelpers.AddQueryString(httpRequestMessage.RequestUri.ToString(), httpRequest.GetQueryCollectionAsDictionary());
                httpRequestMessage.RequestUri = new Uri(httpWorkerUri);

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
                HttpRequestMessage httpRequestMessage = new HttpRequestMessage();
                AddRequestHeadersAndSetRequestUri(httpRequestMessage, scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId.ToString());
                httpRequestMessage.Method = HttpMethod.Post;
                httpRequestMessage.Content = new ObjectContent<HttpScriptInvocationContext>(httpScriptInvocationContext, new JsonMediaTypeFormatter());

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

        private void AddRequestHeadersAndSetRequestUri(HttpRequestMessage httpRequestMessage, string functionName, string invocationId)
        {
            string pathValue = functionName;
            // _httpWorkerOptions.Type is populated only in customHandler section
            if (httpRequestMessage.RequestUri != null && !string.IsNullOrEmpty(_httpWorkerOptions.Type))
            {
                pathValue = httpRequestMessage.RequestUri.AbsolutePath;
            }
            httpRequestMessage.RequestUri = new Uri(new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port, pathValue).ToString());
            httpRequestMessage.Headers.Add(HttpWorkerConstants.InvocationIdHeaderName, invocationId);
            httpRequestMessage.Headers.Add(HttpWorkerConstants.HostVersionHeaderName, ScriptHost.Version);
            httpRequestMessage.Headers.UserAgent.ParseAdd($"{HttpWorkerConstants.UserAgentHeaderValue}/{ScriptHost.Version}");
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
        }
    }
}
