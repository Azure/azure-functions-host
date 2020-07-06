// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
                using (HttpRequestMessage httpRequestMessage = CreateAndGetHttpRequestMessage(scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId.ToString(), new HttpMethod(httpRequest.Method), httpRequest.GetQueryCollectionAsDictionary(), httpRequest.GetRequestUri()))
                {
                    httpRequestMessage.Content = new StreamContent(httpRequest.Body);

                    if (!string.IsNullOrEmpty(httpRequest.ContentType))
                    {
                        httpRequestMessage.Content.Headers.Add("Content-Type", httpRequest.ContentType);
                    }
                    if (httpRequest.ContentLength != null)
                    {
                        httpRequestMessage.Content.Headers.Add("Content-Length", httpRequest.ContentLength.ToString());
                    }

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
                using (HttpRequestMessage httpRequestMessage = CreateAndGetHttpRequestMessage(scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId.ToString(), HttpMethod.Post))
                {
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

        private HttpRequestMessage CreateAndGetHttpRequestMessage(string functionName, string invocationId, HttpMethod requestMethod, IDictionary<string, string> queryCollectionAsDictionary = null, Uri requestUriOverride = null)
        {
            var requestMessage = new HttpRequestMessage();
            AddRequestHeadersAndSetRequestUri(requestMessage, functionName, invocationId);
            if (requestUriOverride != null)
            {
                requestMessage.RequestUri = new Uri(QueryHelpers.AddQueryString(requestUriOverride.ToString(), queryCollectionAsDictionary));
            }
            requestMessage.Method = requestMethod;
            return requestMessage;
        }

        private void AddRequestHeadersAndSetRequestUri(HttpRequestMessage httpRequestMessage, string functionName, string invocationId)
        {
            string pathValue = functionName;
            // _httpWorkerOptions.Type is set to None only in HttpWorker section
            if (httpRequestMessage.RequestUri != null && _httpWorkerOptions.Type != CustomHandlerType.None)
            {
                pathValue = httpRequestMessage.RequestUri.AbsolutePath;
            }
            httpRequestMessage.RequestUri = new Uri(BuildAndGetUri(pathValue));
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
