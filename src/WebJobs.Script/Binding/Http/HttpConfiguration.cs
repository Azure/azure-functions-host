// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;

namespace Microsoft.Azure.WebJobs.Script.Binding.Http
{
    public class HttpConfiguration
    {
        public HttpConfiguration()
        {
            MaxQueueLength = DataflowBlockOptions.Unbounded;
            MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded;
            RoutePrefix = ScriptConstants.DefaultHttpRoutePrefix;
        }

        /// <summary>
        /// Gets or sets the default route prefix that will be applied to
        /// function routes.
        /// </summary>
        public string RoutePrefix { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of pending requests that
        /// will be queued for processing. If this limit is exceeded,
        /// new requests will be rejected with a 429 status code.
        /// </summary>
        public int MaxQueueLength { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of http functions that will execute
        /// in parallel.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether dynamic host counter
        /// checks should be enabled.
        /// </summary>
        public bool DynamicThrottlesEnabled { get; set; }
    }
}
