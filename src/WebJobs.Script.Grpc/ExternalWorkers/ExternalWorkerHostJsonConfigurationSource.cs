// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

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

            JObject hostJsonObject = JObject.Parse(hostJson);
            ProcessObject(hostJsonObject);
        }

        private void ProcessObject(JObject json)
        {
            foreach (JProperty property in json.Properties())
            {
                _path.Push(property.Name);
                ProcessToken(property.Value);
                _path.Pop();
            }
        }

        private void ProcessToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    ProcessObject(token.Value<JObject>());
                    break;
                case JTokenType.Array:
                    ProcessArray(token.Value<JArray>());
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Null:
                case JTokenType.Date:
                case JTokenType.Raw:
                case JTokenType.Bytes:
                case JTokenType.TimeSpan:
                    string key = ConfigurationSectionNames.JobHost
                        + ConfigurationPath.KeyDelimiter
                        + ConfigurationPath.Combine(_path.Reverse());
                    Data[key] = token.Value<JValue>().ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    break;
            }
        }

        private void ProcessArray(JArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                _path.Push(i.ToString());
                ProcessToken(array[i]);
                _path.Pop();
            }
        }
    }
}
