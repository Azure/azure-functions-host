// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Binding.Http
{
    /// <summary>
    /// Provides http request buffering/throttling capabilities.
    /// </summary>
    public class HttpRequestManager
    {
        private ActionBlock<HttpRequestItem> _requestQueue;

        public HttpRequestManager(HttpConfiguration httpConfiguration, TraceWriter traceWriter)
        {
            Config = httpConfiguration;
            TraceWriter = traceWriter;

            if (Config.MaxQueueLength != DataflowBlockOptions.Unbounded ||
                Config.MaxDegreeOfParallelism != DataflowBlockOptions.Unbounded)
            {
                InitializeRequestQueue();
            }
        }

        protected HttpConfiguration Config { get; }

        protected TraceWriter TraceWriter { get; }

        public async Task<HttpResponseMessage> ProcessRequestAsync(HttpRequestMessage request, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> processRequestHandler, CancellationToken cancellationToken)
        {
            if (RejectAllRequests())
            {
                // request cannot be queued or processed at this time,
                return RejectRequest(request);
            }

            if (_requestQueue != null)
            {
                // enqueue the workitem
                var item = new HttpRequestItem
                {
                    Request = request,
                    ProcessRequestHandler = processRequestHandler,
                    CancellationToken = cancellationToken,
                    CompletionSource = new TaskCompletionSource<HttpResponseMessage>()
                };
                if (_requestQueue.Post(item))
                {
                    return await item.CompletionSource.Task;
                }
                else
                {
                    TraceWriter.Info($"Http request queue limit of {Config.MaxQueueLength} has been exceeded.");
                    return RejectRequest(request);
                }
            }
            else
            {
                // queue is not enabled, so just dispatch the request directly
                return await processRequestHandler(request, cancellationToken);
            }
        }

        /// <summary>
        /// For a request that will be rejected due to load, max queue length
        /// exceeded, etc. this method will be called, allowing the
        /// status code, headers, etc. for the request to be configured.
        /// </summary>
        /// <param name="request">The request to reject.</param>
        /// <returns>The response to return.</returns>
        protected virtual HttpResponseMessage RejectRequest(HttpRequestMessage request)
        {
            return new HttpResponseMessage((HttpStatusCode)429);
        }

        /// <summary>
        /// Returns a value indicating whether all incoming requests
        /// should be rejected, for example due to host overload, etc.
        /// </summary>
        /// <returns>True if requests shoudl be rejected.</returns>
        protected virtual bool RejectAllRequests()
        {
            return false;
        }

        private void InitializeRequestQueue()
        {
            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Config.MaxDegreeOfParallelism,
                BoundedCapacity = Config.MaxQueueLength
            };

            _requestQueue = new ActionBlock<HttpRequestItem>(async item =>
            {
                try
                {
                    var response = await item.ProcessRequestHandler(item.Request, item.CancellationToken);
                    item.CompletionSource.SetResult(response);
                }
                catch (Exception ex)
                {
                    item.CompletionSource.SetException(ex);
                }
            }, options);
        }

        private class HttpRequestItem
        {
            /// <summary>
            /// Gets or sets the request to process.
            /// </summary>
            public HttpRequestMessage Request { get; set; }

            /// <summary>
            /// Gets or sets the process method for the request.
            /// </summary>
            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> ProcessRequestHandler { get; set; }

            /// <summary>
            /// Gets or sets the cancellation token.
            /// </summary>
            public CancellationToken CancellationToken { get; set; }

            /// <summary>
            /// Gets or sets the completion source to use.
            /// </summary>
            public TaskCompletionSource<HttpResponseMessage> CompletionSource { get; set; }
        }
    }
}
