// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Encapsulates an http request queue used for request throttling. See <see cref="HttpThrottleMiddleware"/>.
    /// This has been factored as its own service to ensure that it's lifetime stays tied to host the host
    /// instance lifetime, not the middleware lifetime which is longer lived.
    /// </summary>
    internal class HttpRequestQueue
    {
        private readonly IOptions<HttpOptions> _httpOptions;
        private ActionBlock<HttpRequestItem> _requestQueue;

        public HttpRequestQueue(IOptions<HttpOptions> httpOptions)
        {
            _httpOptions = httpOptions;

            if (_httpOptions.Value.MaxOutstandingRequests != DataflowBlockOptions.Unbounded ||
                _httpOptions.Value.MaxConcurrentRequests != DataflowBlockOptions.Unbounded)
            {
                InitializeRequestQueue();
            }
        }

        /// <summary>
        /// Gets a value indicating whether request queueing is enabled.
        /// </summary>
        public bool Enabled => _requestQueue != null;

        public async Task<bool> Post(HttpContext httpContext, RequestDelegate next)
        {
            // enqueue the request workitem
            var item = new HttpRequestItem
            {
                HttpContext = httpContext,
                Next = next,
                CompletionSource = new TaskCompletionSource<object>(),
                ExecutionContext = System.Threading.ExecutionContext.Capture()
            };

            if (_requestQueue.Post(item))
            {
                await item.CompletionSource.Task;
                return true;
            }
            else
            {
                // no more requests can be queued at this time
                return false;
            }
        }

        private void InitializeRequestQueue()
        {
            // if throttles are enabled, initialize the queue
            var blockOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _httpOptions.Value.MaxConcurrentRequests,
                BoundedCapacity = _httpOptions.Value.MaxOutstandingRequests
            };

            _requestQueue = new ActionBlock<HttpRequestItem>(async item =>
            {
                TaskCompletionSource<object> complete = new TaskCompletionSource<object>();

                System.Threading.ExecutionContext.Run(item.ExecutionContext, async _ =>
                {
                    try
                    {
                        await item.Next.Invoke(item.HttpContext);
                        item.CompletionSource.SetResult(null);
                    }
                    catch (Exception ex)
                    {
                        item.CompletionSource.SetException(ex);
                    }
                    finally
                    {
                        complete.SetResult(null);
                    }
                }, null);

                await complete.Task;
            }, blockOptions);
        }

        private class HttpRequestItem
        {
            /// <summary>
            /// Gets or sets the request context to process.
            /// </summary>
            public HttpContext HttpContext { get; set; }

            /// <summary>
            /// Gets or sets the completion delegate for the request.
            /// </summary>
            public RequestDelegate Next { get; set; }

            /// <summary>
            /// Gets or sets the completion source to use.
            /// </summary>
            public TaskCompletionSource<object> CompletionSource { get; set; }

            public System.Threading.ExecutionContext ExecutionContext { get; set; }
        }
    }
}
