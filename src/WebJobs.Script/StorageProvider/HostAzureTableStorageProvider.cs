// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class HostAzureTableStorageProvider : IAzureTableStorageProvider
    {
        private readonly ILogger<HostAzureTableStorageProvider> _logger;
        private readonly TableServiceClientProvider _tableServiceClientProvider;

        public HostAzureTableStorageProvider(IConfiguration configuration, ILogger<HostAzureTableStorageProvider> logger, AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tableServiceClientProvider = new TableServiceClientProvider(componentFactory, logForwarder);
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public virtual IConfiguration Configuration { get; private set; }

        public virtual bool TryCreateTableServiceClientFromConnection(string connection, out TableServiceClient client)
        {
            var connectionToUse = connection ?? ConnectionStringNames.Storage;

            try
            {
                client = _tableServiceClientProvider.Create(connectionToUse, Configuration);
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
