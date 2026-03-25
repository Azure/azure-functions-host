// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// An <see cref="IConfigurationSource"/> that provides host.json configuration delivered
    /// by an external worker via gRPC capabilities, replacing <c>HostJsonFileConfigurationSource</c>
    /// when external worker mode is enabled.
    /// </summary>
    internal class ExternalWorkerHostJsonConfigurationSource : IConfigurationSource
    {
        private readonly HostJsonContentProvider _contentProvider;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalWorkerHostJsonConfigurationSource"/> class.
        /// </summary>
        /// <param name="contentProvider">The provider that delivers host.json content from the external worker.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        public ExternalWorkerHostJsonConfigurationSource(HostJsonContentProvider contentProvider, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(contentProvider);
            ArgumentNullException.ThrowIfNull(logger);

            _contentProvider = contentProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ExternalWorkerHostJsonConfigurationProvider(_contentProvider, _logger);
        }
    }

    /// <summary>
    /// A <see cref="ConfigurationProvider"/> that reads host.json content from an external worker
    /// and flattens it into configuration keys prefixed with
    /// <see cref="ConfigurationSectionNames.JobHost"/> (<c>AzureFunctionsJobHost</c>).
    /// <para>
    /// The key structure mirrors <c>HostJsonFileConfigurationProvider</c> so that downstream
    /// consumers see an identical configuration shape regardless of the source.
    /// </para>
    /// <example>
    /// Given the following host.json:
    /// <code>
    /// { "logging": { "logLevel": { "default": "Information" } } }
    /// </code>
    /// The flattened keys are:
    /// <code>
    /// AzureFunctionsJobHost:logging:logLevel:default = Information
    /// </code>
    /// </example>
    /// </summary>
    internal class ExternalWorkerHostJsonConfigurationProvider : ConfigurationProvider
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        private readonly HostJsonContentProvider _contentProvider;
        private readonly Stack<string> _path = new();
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalWorkerHostJsonConfigurationProvider"/> class.
        /// </summary>
        /// <param name="contentProvider">The provider that delivers host.json content from the external worker.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        public ExternalWorkerHostJsonConfigurationProvider(HostJsonContentProvider contentProvider, ILogger logger)
        {
            _contentProvider = contentProvider;
            _logger = logger;
        }

        /// <summary>
        /// Loads host.json content from the external worker and flattens the JSON into the
        /// <see cref="ConfigurationProvider.Data"/> dictionary.
        /// </summary>
        public override void Load()
        {
            string hostJson = _contentProvider.WaitForContent(DefaultTimeout);

            _logger.LogInformation("Applying host.json configuration received from external worker.");

            // TODO: Align with HostJsonFileConfigurationProvider for production parity:
            // - Apply HostConfigurationProfile settings before processing host.json
            //   (GetConfigProfile loads profile-based overrides from the "configurationProfile" key)
            // - Validate the "version" field (must be "2.0")
            // - Handle "isDefaultHostConfig" flag (controls extension bundle defaults)
            // - Add metrics logging (MetricEventNames.LoadHostConfigurationSource)

            using JsonDocument doc = JsonDocument.Parse(hostJson);
            ProcessElement(doc.RootElement);
        }

        private void ProcessElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        _path.Push(property.Name);
                        ProcessElement(property.Value);
                        _path.Pop();
                    }
                    break;

                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        _path.Push(index.ToString());
                        ProcessElement(item);
                        _path.Pop();
                        index++;
                    }
                    break;

                case JsonValueKind.String:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    string key = ConfigurationSectionNames.JobHost
                        + ConfigurationPath.KeyDelimiter
                        + ConfigurationPath.Combine(_path.Reverse());
                    Data[key] = element.ValueKind is JsonValueKind.Null
                        ? null
                        : element.ToString();
                    break;
            }
        }
    }
}
