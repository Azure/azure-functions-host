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
    internal class HostAzureTableStorageProvider : IAzureTableStorageProvider
    {
        private readonly ILogger<HostAzureTableStorageProvider> _logger;
        private readonly IDelegatingHandlerProvider _delegatingHandlerProvider;
        private readonly TableServiceClientProvider _tableServiceClientProvider;

        public HostAzureTableStorageProvider(IConfiguration configuration, ILogger<HostAzureTableStorageProvider> logger, AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder, IDelegatingHandlerProvider delegatingHandlerProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _delegatingHandlerProvider = delegatingHandlerProvider;
            _tableServiceClientProvider = new TableServiceClientProvider(componentFactory, logForwarder);
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public virtual IConfiguration Configuration { get; private set; }

        public virtual bool TryCreateTableServiceClientFromConnection(string connection, out TableServiceClient client)
        {
            var connectionToUse = connection ?? ConnectionStringNames.Storage;

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

                client = _tableServiceClientProvider.Create(connectionToUse, Configuration, options);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogDebug($"Could not create TableServiceClient. Exception: {e}");
                client = default;
                return false;
            }
        }

        public virtual bool TryCreateHostingTableServiceClient(out TableServiceClient client) => TryCreateTableServiceClientFromConnection(ConnectionStringNames.Storage, out client);
    }
}
