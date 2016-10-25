﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    // Adapter for capturing SDK events and logging them to tables.
    internal class FastLogger : IAsyncCollector<FunctionInstanceLogEntry>
    {
        private readonly ILogWriter _writer;

        public FastLogger(string hostName, string accountConnectionString)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
            var client = account.CreateCloudTableClient();
            var tableProvider = LogFactory.NewLogTableProvider(client);
                        
            string containerName = Environment.MachineName;
            this._writer = LogFactory.NewWriter(hostName, containerName, tableProvider);
        }

        public async Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Convert Host to Protocol so we can log it 
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            var jsonClone = JsonConvert.SerializeObject(item, settings);
            var item2 = JsonConvert.DeserializeObject<FunctionInstanceLogItem>(jsonClone);
            item2.FunctionName = Utility.GetFunctionShortName(item2.FunctionName);
            await _writer.AddAsync(item2);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _writer.FlushAsync();
        }
    }
}
