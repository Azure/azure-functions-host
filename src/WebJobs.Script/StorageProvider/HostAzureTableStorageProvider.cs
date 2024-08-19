// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    internal sealed class HostAzureTableStorageProvider : IAzureTableStorageProvider
    {
        private readonly ILogger<HostAzureTableStorageProvider> _logger;
        private readonly IDelegatingHandlerProvider _delegatingHandlerProvider;
        private readonly TableServiceClientProvider _tableServiceClientProvider;
        private readonly IConfiguration _configuration;

        public HostAzureTableStorageProvider(IConfiguration configuration, ILogger<HostAzureTableStorageProvider> logger, AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder, IDelegatingHandlerProvider delegatingHandlerProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _delegatingHandlerProvider = delegatingHandlerProvider;
            _tableServiceClientProvider = new TableServiceClientProvider(componentFactory, logForwarder);
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public bool TryCreateTableServiceClient(string connection, out TableServiceClient client)
        {
            if (string.IsNullOrEmpty(connection))
            {
                throw new ArgumentException($"'{nameof(connection)}' cannot be null or empty.", nameof(connection));
            }

            try
            {
                DelegatingHandler handler = _delegatingHandlerProvider?.Create();
                TableClientOptions options = null;

                if (handler != null)
                {
                    options = new TableClientOptions
                    {
                        Transport = new HttpClientTransport(handler)
                    };
                }

                client = _tableServiceClientProvider.Create(connection, _configuration, options);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Could not create TableServiceClient. Exception: {message}", e.Message);
                client = default;
                return false;
            }
        }

        public bool TryCreateHostingTableServiceClient(out TableServiceClient client) => TryCreateTableServiceClient(ConnectionStringNames.Storage, out client);
    }
}
