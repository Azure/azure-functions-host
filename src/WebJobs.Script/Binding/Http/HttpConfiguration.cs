// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;

namespace Microsoft.Azure.WebJobs.Script.Binding.Http
{
    public class HttpConfiguration
    {
        public HttpConfiguration()
        {
            MaxOutstandingRequests = DataflowBlockOptions.Unbounded;
            MaxConcurrentRequests = DataflowBlockOptions.Unbounded;
            RoutePrefix = ScriptConstants.DefaultHttpRoutePrefix;
        }

        /// <summary>
        /// Gets or sets the default route prefix that will be applied to
        /// function routes.
        /// </summary>
        public string RoutePrefix { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of outstanding requests that
        /// will be held at any given time. This limit includes requests
        /// that have started executing, as well as requests that have
        /// not yet started executing.
        /// If this limit is exceeded, new requests will be rejected with a 429 status code.
        /// </summary>
        public int MaxOutstandingRequests { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of http functions that will
        /// be allowed to execute in parallel.
        /// </summary>
        public int MaxConcurrentRequests { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether dynamic host counter
        /// checks should be enabled.
        /// </summary>
        public bool DynamicThrottlesEnabled { get; set; }
    }
}
