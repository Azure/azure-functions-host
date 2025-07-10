// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public class HttpWorkerOptions : IOptionsFormatter
    {
        public CustomHandlerType Type { get; set; }

        public HttpWorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public int Port { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host will forward the request to the worker process.
        /// </summary>
        /// <remarks>
        /// The host will rebuild the initial invocation HTTP Request and send the copy to the worker process.
        /// </remarks>
        public bool EnableForwardingHttpRequest { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host will proxy the invocation HTTP request to the worker process.
        /// </summary>
        public bool EnableProxyingHttpRequest { get; set; }

        public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(HttpWorkerOptions), HttpWorkerOptionsJsonSerializerContext.Default);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(HttpWorkerOptions))]
    internal partial class HttpWorkerOptionsJsonSerializerContext : JsonSerializerContext;
}