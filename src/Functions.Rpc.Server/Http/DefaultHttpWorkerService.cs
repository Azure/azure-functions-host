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
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class DefaultHttpWorkerService : IHttpWorkerService
    {
        private readonly HttpClient _httpClient;
        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly ILogger _logger;
        private readonly bool _enableRequestTracing;
        private readonly IHttpProxyService _httpProxyService;
        private readonly Uri _destinationPrefix;
        private readonly string _userAgentString;

        public DefaultHttpWorkerService(IOptions<HttpWorkerOptions> httpWorkerOptions, ILoggerFactory loggerFactory, IEnvironment environment,
            IOptions<ScriptJobHostOptions> scriptHostOptions, IHttpProxyService httpProxyService)
            : this(CreateHttpClient(httpWorkerOptions), httpWorkerOptions, loggerFactory.CreateLogger<DefaultHttpWorkerService>(), environment, scriptHostOptions, httpProxyService)
        {
        }

        internal DefaultHttpWorkerService(HttpClient httpClient, IOptions<HttpWorkerOptions> httpWorkerOptions, ILogger logger, IEnvironment environment,
            IOptions<ScriptJobHostOptions> scriptHostOptions, IHttpProxyService httpProxyService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpWorkerOptions = httpWorkerOptions.Value ?? throw new ArgumentNullException(nameof(httpWorkerOptions.Value));
            _httpProxyService = httpProxyService ?? throw new ArgumentNullException(nameof(httpProxyService));
            _enableRequestTracing = environment.IsCoreTools();
            if (scriptHostOptions.Value.FunctionTimeout == null)
            {
                _logger.JobHostFunctionTimeoutNotSet();
                // Default to MaxValue if FunctionTimeout is not set in host.json or is set to -1.
                _httpClient.Timeout = TimeSpan.FromMilliseconds(int.MaxValue);
            }
            else
            {
                // Set 1 minute greater than FunctionTimeout to ensure invoction failure due to timeout is raised before httpClient raises operation cancelled exception
                _httpClient.Timeout = scriptHostOptions.Value.FunctionTimeout.Value.Add(TimeSpan.FromMinutes(1));
            }

            _destinationPrefix = new UriBuilder(WorkerConstants.HttpScheme, WorkerConstants.HostName, _httpWorkerOptions.Port).Uri;
            _userAgentString = $"{HttpWorkerConstants.UserAgentHeaderValue}/{ScriptHost.Version}";
        }

        private static HttpClient CreateHttpClient(IOptions<HttpWorkerOptions> httpWorkerOptions)
        {
            HttpClientHandler handler = new();
            handler.UseCookies = false;
            handler.AllowAutoRedirect = !httpWorkerOptions.Value.EnableForwardingHttpRequest;
            return new(handler);
        }

        public Task InvokeAsync(ScriptInvocationContext scriptInvocationContext)
        {
            if (scriptInvocationContext.FunctionMetadata.IsHttpInAndOutFunction())
            {
                if (_httpWorkerOptions.EnableProxyingHttpRequest)
                {
                    return ProxyInvocationRequest(scriptInvocationContext);
                }

                // type is empty for httpWorker section. EnableForwardingHttpRequest is opt-in for custom handler section.
                if (_httpWorkerOptions.Type == CustomHandlerType.None || _httpWorkerOptions.EnableForwardingHttpRequest)
                {
                    return ProcessHttpInAndOutInvocationRequest(scriptInvocationContext);
                }
            }

            return ProcessDefaultInvocationRequest(scriptInvocationContext);
        }

        internal async Task ProxyInvocationRequest(ScriptInvocationContext scriptInvocationContext)
        {
            try
            {
                if (!scriptInvocationContext.TryGetHttpRequest(out HttpRequest httpRequest))
                {
                    throw new InvalidOperationException($"Cannot proxy the HttpTrigger function {scriptInvocationContext.FunctionMetadata.Name} without an input of type {nameof(HttpRequest)}.");
                }

                AddProxyingHeaders(httpRequest, scriptInvocationContext.ExecutionContext.InvocationId.ToString());

                // YARP only requires the destination prefix. The path and query string are added by the YARP proxy during SendAsync using info from the HttpContext.
                _httpProxyService.StartForwarding(scriptInvocationContext, _destinationPrefix);

                await _httpProxyService.EnsureSuccessfulForwardingAsync(scriptInvocationContext); // this will throw if forwarding is unsuccessful

                // this needs to be removed to avoid downstream errors since the HttpResponse is already returned by proxying
                if (scriptInvocationContext.BindingData.Any(p => string.Equals(ScriptConstants.SystemReturnParameterBindingName, p.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    scriptInvocationContext.BindingData[ScriptConstants.SystemReturnParameterBindingName] = null;
                }

                scriptInvocationContext.ResultSource.SetResult(ScriptInvocationResult.Success);
            }
            catch (Exception exc)
            {
                scriptInvocationContext.ResultSource.TrySetException(exc);
            }
        }

        internal async Task ProcessHttpInAndOutInvocationRequest(ScriptInvocationContext scriptInvocationContext)
        {
            _logger.CustomHandlerForwardingHttpTriggerInvocation(scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);

            ScriptInvocationResult scriptInvocationResult = new ScriptInvocationResult()
            {
                Outputs = new Dictionary<string, object>()
            };

            var input = scriptInvocationContext.Inputs.First();

            HttpRequest httpRequest = input.Val as HttpRequest;
            if (httpRequest == null)
            {
                throw new InvalidOperationException($"HttpTrigger value for: `{input.Name}` is null");
            }

            try
            {
                string uriPathValue = GetPathValue(_httpWorkerOptions, scriptInvocationContext.FunctionMetadata.Name, httpRequest);
                string uri = BuildAndGetUri(uriPathValue);

                using (HttpRequestMessage httpRequestMessage = httpRequest.ToHttpRequestMessage(uri))
                {
                    AddHeaders(httpRequestMessage, scriptInvocationContext.ExecutionContext.InvocationId.ToString());

                    HttpResponseMessage invocationResponse = await SendInvocationRequestAsync(scriptInvocationContext, httpRequestMessage);

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

        private async Task<HttpResponseMessage> SendInvocationRequestAsync(ScriptInvocationContext scriptInvocationContext, HttpRequestMessage httpRequestMessage)
        {
            // Only log Request / Response when running locally
            if (_enableRequestTracing)
            {
                scriptInvocationContext.Logger.LogTrace($"Invocation Request:{httpRequestMessage}");
                await LogHttpContent(scriptInvocationContext, httpRequestMessage.Content);
            }
            _logger.CustomHandlerSendingInvocation(scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
            HttpResponseMessage invocationResponse = await _httpClient.SendAsync(httpRequestMessage);
            if (_enableRequestTracing)
            {
                scriptInvocationContext.Logger.LogTrace($"Invocation Response:{invocationResponse}");
                await LogHttpContent(scriptInvocationContext, invocationResponse.Content);
            }
            _logger.CustomHandlerReceivedInvocationResponse(scriptInvocationContext.FunctionMetadata.Name, scriptInvocationContext.ExecutionContext.InvocationId);
            return invocationResponse;
        }

        private static async Task LogHttpContent(ScriptInvocationContext scriptInvocationContext, HttpContent httpContent)
        {
            string stringContent = await GetHttpContentAsString(httpContent);
            if (!string.IsNullOrEmpty(stringContent))
            {
                scriptInvocationContext.Logger.LogTrace($"{stringContent}");
            }
        }

        private static async Task<string> GetHttpContentAsString(HttpContent content)
        {
            if (content != null)
            {
                bool isMediaTypeOctetOrMultipart = content.Headers != null && Utility.IsMediaTypeOctetOrMultipart(content.Headers.ContentType);
                // do not log binary data as string
                if (!isMediaTypeOctetOrMultipart)
                {
                    return await content.ReadAsStringAsync();
                }
            }
            return null;
        }

        internal void AddHeaders(HttpRequestMessage httpRequest, string invocationId)
        {
            httpRequest.Headers.Add(HttpWorkerConstants.HostVersionHeaderName, ScriptHost.Version);
            httpRequest.Headers.Add(HttpWorkerConstants.InvocationIdHeaderName, invocationId);
            httpRequest.Headers.UserAgent.ParseAdd(_userAgentString);
        }

        private void AddProxyingHeaders(HttpRequest httpRequest, string invocationId)
        {
            // if there are existing headers, override them
            httpRequest.Headers[HttpWorkerConstants.HostVersionHeaderName] = ScriptHost.Version;
            httpRequest.Headers[HttpWorkerConstants.InvocationIdHeaderName] = invocationId;
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

                    HttpResponseMessage invocationResponse = await SendInvocationRequestAsync(scriptInvocationContext, httpRequestMessage);

                    // Only process output bindings if response is success code
                    invocationResponse.EnsureSuccessStatusCode();

                    HttpScriptInvocationResult httpScriptInvocationResult = await GetHttpScriptInvocationResult(invocationResponse);

                    if (httpScriptInvocationResult != null)
                    {
                        if (httpScriptInvocationResult.Outputs == null || !httpScriptInvocationResult.Outputs.Any())
                        {
                            _logger.LogDebug("Outputs not set on http response for invocationId:{invocationId}", scriptInvocationContext.ExecutionContext.InvocationId);
                        }
                        if (httpScriptInvocationResult.ReturnValue == null)
                        {
                            _logger.LogDebug("ReturnValue not set on http response for invocationId:{invocationId}", scriptInvocationContext.ExecutionContext.InvocationId);
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

        internal async Task<HttpScriptInvocationResult> GetHttpScriptInvocationResult(HttpResponseMessage httpResponseMessage)
        {
            try
            {
                return await httpResponseMessage.Content.ReadAsAsync<HttpScriptInvocationResult>();
            }
            catch (Exception ex)
            {
                var exMessage = $"Invalid HttpResponseMessage:\n{httpResponseMessage}";
                string httpContent = await GetHttpContentAsString(httpResponseMessage.Content);
                if (!string.IsNullOrEmpty(httpContent))
                {
                    exMessage = $"{exMessage}\n {Sanitizer.Sanitize(httpContent)}";
                }
                throw new InvalidOperationException(exMessage, ex);
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
            TimeSpan pollingInterval = TimeSpan.FromMilliseconds(WorkerConstants.WorkerReadyCheckPollingIntervalMilliseconds);
            bool continueWaitingForWorker = await Utility.DelayAsync(_httpWorkerOptions.InitializationTimeout, pollingInterval, async () =>
            {
                string requestUri = BuildAndGetUri();
                try
                {
                    await SendPingRequestAsync(requestUri);
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

        private async Task<HttpResponseMessage> SendPingRequestAsync(string requestUri, HttpMethod method = null)
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
                HttpResponseMessage response = await SendPingRequestAsync(requestUri, HttpMethod.Get);
                _logger.LogDebug($"Response code while pinging uri '{requestUri}' is '{response.StatusCode}'");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Pinging uri '{requestUri}' resulted in exception", ex);
            }
        }
    }
}
