// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IAzureTableStorageProvider
    {
        IConfiguration Configuration { get; }

        bool TryCreateHostingTableServiceClient(out TableServiceClient client);

        bool TryCreateTableServiceClientFromConnection(string connection, out TableServiceClient client);
    }
}