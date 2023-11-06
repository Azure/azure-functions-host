// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventRepositoryFactory : IDiagnosticEventRepositoryFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public DiagnosticEventRepositoryFactory(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        public IDiagnosticEventRepository Create()
        {
            // Using this to break ciruclar dependency for ILoggers. Typically you cannot log errors within the logging pipeline because it creates infinte loop.
            // However in this case that loop is broken because of the filtering in the DiagnosticEventLogger

            string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                return new DiagnosticEventNullRepository();
            }
            else
            {
                return _serviceProvider.GetRequiredService<IDiagnosticEventRepository>();
            }
        }
    }
}