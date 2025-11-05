// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpWorkerOptions : IOptionsFormatter
    {
        private int? _port;

        public CustomHandlerType Type { get; set; }

        public HttpWorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public int Port
        {
            get
            {
                if (_port is null)
                {
                    _port = WorkerUtilities.GetUnusedTcpPort(); // Will always be realized during Options setup.
                    IsPortManuallySet = false;
                }

                return _port.Value;
            }

            set
            {
                // During dynamic allocation of port, the get method will be called before set method and _port will be assigned dynamically.
                // Adding a check here to make sure we don't override IsPortManuallySet flag in that case.
                if (_port != value)
                {
                    IsPortManuallySet = true;
                    _port = value;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="Port"/> property value is taken from configuration.
        /// True value indicates that the host will use the configured port value rather than allocating a dynamic port.
        /// </summary>
        public bool IsPortManuallySet { get; private set; }

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

        /// <summary>
        /// Gets or sets a value indicating whether custom routes are enabled.
        /// </summary>
        public bool CustomRoutesEnabled { get; set; }

        /// <summary>
        /// Gets or sets http configuration.
        /// </summary>
        public CustomHandlerHttpOptions Http { get; set; }

        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(HttpWorkerOptions), HttpWorkerOptionsJsonSerializerContext.Default);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(HttpWorkerOptions))]
    internal partial class HttpWorkerOptionsJsonSerializerContext : JsonSerializerContext;
}